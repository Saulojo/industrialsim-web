using IndustrialSim.Sessions;
using IndustrialSim.Sessions.Modbus;
using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Server;

/// <summary>
/// Liga as plantas em execução ao host (seção <c>Simulation</c> do appsettings): catálogo de cenários, laço de
/// tempo real, gerenciador de plantas (com o Modbus de cada planta) e o serviço que inicia e para os dois junto
/// com o processo.
/// </summary>
internal static class SimulationServices
{
    public const string SectionName = "Simulation";

    public static IServiceCollection AddSimulation(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        IConfigurationSection section = configuration.GetSection(SectionName);

        services.AddSingleton(_ => section.GetSection("Loop").Get<RealTimeLoopOptions>() ?? new RealTimeLoopOptions());
        services.AddSingleton(_ => section.GetSection("Socket").Get<PlantSocketOptions>() ?? new PlantSocketOptions());

        services.AddSingleton(_ =>
        {
            InstanceManagerOptions options = section.GetSection("Instances").Get<InstanceManagerOptions>() ?? new InstanceManagerOptions();
            ModbusEndpointOptions modbus = section.GetSection("Modbus").Get<ModbusEndpointOptions>() ?? new ModbusEndpointOptions();

            // Gravações relativas à raiz de conteúdo do host.
            return options with
            {
                RecordingDirectory = Path.GetFullPath(options.RecordingDirectory, environment.ContentRootPath),
                Modbus = modbus,
            };
        });

        // O laço usa sempre o relógio do sistema (Stopwatch); o TimeProvider registrado vale para horários e expiração.
        services.AddSingleton(sp => new RealTimeLoop(
            sp.GetRequiredService<RealTimeLoopOptions>(),
            sp.GetRequiredService<ILogger<RealTimeLoop>>()));

        services.AddSingleton(sp =>
        {
            // Cenários relativos à pasta do programa, para onde o build copia scenarios/.
            string directory = Path.GetFullPath(section["ScenarioDirectory"] ?? "scenarios", AppContext.BaseDirectory);
            return ScenarioCatalog.Load(
                directory,
                sp.GetRequiredService<RealTimeLoopOptions>().StepSeconds,
                sp.GetRequiredService<ILogger<ScenarioCatalog>>());
        });

        services.AddSingleton(sp => new InstanceManager(
            sp.GetRequiredService<ScenarioCatalog>(),
            sp.GetRequiredService<RealTimeLoop>(),
            sp.GetRequiredService<InstanceManagerOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<InstanceManager>>()));

        services.AddHostedService<SimulationHostedService>();
        return services;
    }
}

/// <summary>
/// Inicia o laço e a verificação de expiração com o host e, ao parar o host (Ctrl+C ou parada do serviço),
/// encerra todas as plantas e espera as threads do laço terminarem.
/// </summary>
internal sealed class SimulationHostedService(RealTimeLoop loop, InstanceManager manager) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        loop.Start();
        manager.StartExpiryTimer();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        manager.Dispose();
        loop.Dispose();
        return Task.CompletedTask;
    }
}
