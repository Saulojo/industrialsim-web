using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// Servidor de teste com as plantas em execução, configurado por chaves do appsettings; as gravações vão para
/// uma pasta temporária, apagada no fim.
/// </summary>
internal sealed class SimulationFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string> _settings;

    public SimulationFactory(params (string Key, string Value)[] settings)
    {
        RecordingDirectory = Path.Combine(Path.GetTempPath(), "industrialsim-api-" + Guid.NewGuid().ToString("N"));
        _settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Simulation:Instances:RecordingDirectory"] = RecordingDirectory,
        };

        foreach (var (key, value) in settings)
        {
            _settings[key] = value;
        }
    }

    public string RecordingDirectory { get; }

    /// <summary>Serviços extras do teste (ex.: um <c>IStartupFilter</c> que põe middleware antes do app).</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        if (ConfigureTestServices is not null)
        {
            builder.ConfigureServices(ConfigureTestServices);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(RecordingDirectory))
        {
            Directory.Delete(RecordingDirectory, recursive: true);
        }
    }
}
