extern alias simrun;

using System.Net;
using System.Net.Http.Json;
using IndustrialSim.Contracts;
using simrun::IndustrialSim.SimRun.NetworkLoad;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// <c>simrun carga-rede</c> (Etapa 9A) contra o servidor inteiro em memória, com o laço rodando e mestres Modbus de
/// verdade em loopback. Patamares curtos e sem conferir as metas de tempo real (o CI roda em máquinas
/// compartilhadas): o teste de 30 minutos é feito na VM e registrado nas evidências.
/// </summary>
public class NetworkLoadTests(ITestOutputHelper output)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APhase_CreatesPlantsRunsAuthorizedMastersAndCleansUp()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();
        using var log = new StringWriter();
        var runner = new NetworkLoadRunner(new LoadServerClient(client), IPAddress.Loopback, IPAddress.Loopback, remote: null, log);

        PhaseReport phase = await runner.RunPhaseAsync(3, "caldeira", TimeSpan.FromSeconds(3), Cancel);
        output.WriteLine(log.ToString());
        output.WriteLine(phase.ToString());

        Assert.Null(phase.CreateError);
        Assert.Equal(3, phase.PlantsCreated);
        Assert.Equal(0, phase.ModbusErrors);
        Assert.Equal(0, phase.HttpErrors);
        Assert.Equal(0, phase.PlantFaults);

        // 3 mestres a 10 Hz por 3 s: perto de 90 varreduras; a margem cobre a máquina lenta do CI.
        Assert.InRange(phase.ModbusScans, 30, 100);
        Assert.NotNull(phase.Loop);
        Assert.Equal(3, phase.Loop.PlantCount);
        Assert.Null(phase.Resources);
        Assert.DoesNotContain(phase.Failures, f => f.StartsWith("erros", StringComparison.Ordinal));

        // As plantas do patamar foram encerradas.
        Assert.Empty((await client.GetFromJsonAsync<PlantSummary[]>("/api/plants", Cancel))!);
    }

    [Fact]
    public async Task AMasterThatIsNotAuthorized_CountsErrors()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();

        // Autoriza 127.0.0.2, mas o mestre conecta de 127.0.0.1: a conexão fica no pareamento, sem resposta (D38).
        var runner = new NetworkLoadRunner(new LoadServerClient(client), IPAddress.Loopback, IPAddress.Parse("127.0.0.2"), remote: null, TextWriter.Null);

        PhaseReport phase = await runner.RunPhaseAsync(1, "caldeira", TimeSpan.FromSeconds(3), Cancel);

        Assert.Equal(0, phase.ModbusScans);
        Assert.True(phase.ModbusErrors > 0);
        Assert.False(phase.Passed);
        Assert.Contains(phase.Failures, f => f.StartsWith("nenhuma varredura", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheInstanceLimit_InterruptsThePhaseAndCleansUp()
    {
        using var factory = new SimulationFactory(("Simulation:Instances:MaxInstances", "2"));
        using HttpClient client = factory.CreateClient();
        var runner = new NetworkLoadRunner(new LoadServerClient(client), IPAddress.Loopback, IPAddress.Loopback, remote: null, TextWriter.Null);

        PhaseReport phase = await runner.RunPhaseAsync(3, "caldeira", TimeSpan.FromSeconds(1), Cancel);

        Assert.Equal(2, phase.PlantsCreated);
        Assert.Equal($"criação da planta 3: {ApiErrorCodes.InstanceLimitReached}", phase.CreateError);
        Assert.False(phase.Passed);
        Assert.Empty((await client.GetFromJsonAsync<PlantSummary[]>("/api/plants", Cancel))!);
    }
}
