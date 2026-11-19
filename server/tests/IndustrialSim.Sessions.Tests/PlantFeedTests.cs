using IndustrialSim.IO;
using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Sessions.Tests;

/// <summary>
/// Etapa 7: o que a IHM acompanha e comanda numa planta: estado publicado a cada período, alarmes, distúrbio da
/// seção <c>hmi</c> (D39) e reinício (D41). Os passos são dados à mão, sem iniciar o laço.
/// </summary>
public sealed class PlantFeedTests : IDisposable
{
    private static readonly ScenarioCatalog Catalog = ProductScenarios.Catalog();

    private readonly string _recordings = Path.Combine(Path.GetTempPath(), "industrialsim-feed-" + Guid.NewGuid().ToString("N"));
    private readonly RealTimeLoop _loop = new();
    private readonly InstanceManager _manager;

    public PlantFeedTests()
    {
        _manager = new InstanceManager(Catalog, _loop, new InstanceManagerOptions { RecordingDirectory = _recordings });
    }

    [Fact]
    public void Subscription_GetsCurrentStateAtOnce_ThenOneSnapshotEveryFiveSteps()
    {
        PlantInstance plant = Create();
        using PlantSubscription subscription = plant.Subscribe();

        Assert.True(subscription.Reader.TryRead(out PlantSnapshot? first));
        Assert.Equal(0, first.StepCount);
        Assert.Equal(0.05, plant.PublishPeriodSeconds, 12);

        Step(plant, 4);
        Assert.False(subscription.Reader.TryRead(out _));

        Step(plant, 1);
        Assert.True(subscription.Reader.TryRead(out PlantSnapshot? fifth));
        Assert.Equal(5, fifth.StepCount);
        Assert.Equal(0.05, fifth.SimulatedSeconds, 12);
        Assert.Equal(plant.Scenario.Definition.Inputs.Count, fifth.Inputs.Length);
        Assert.Equal(plant.Scenario.Definition.Outputs.Count, fifth.Outputs.Length);
        Assert.Equal(InstanceStatus.Running, fifth.Status);
    }

    /// <summary>Um leitor lento não segura o laço: a fila guarda só o estado mais recente.</summary>
    [Fact]
    public void SlowSubscriber_KeepsOnlyTheLatestSnapshot()
    {
        PlantInstance plant = Create();
        using PlantSubscription subscription = plant.Subscribe();

        Step(plant, 50);

        Assert.True(subscription.Reader.TryRead(out PlantSnapshot? snapshot));
        Assert.Equal(50, snapshot.StepCount);
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public void Disturbance_IsClampedToTheScenarioRange_AndReachesThePlantOnTheNextStep()
    {
        PlantInstance plant = Create();
        int input = plant.Io.Plant.InputIndex("FV-103_command");

        Assert.True(plant.SetDisturbance("FV-103_command", 1.7));
        Assert.Equal(1.0, plant.Io.Plant.GetInput(input));

        Step(plant, 500); // τ = 2 s da válvula: 5 s depois, quase toda aberta
        Assert.True(plant.Io.Plant.GetOutput("FV-103_position") > 0.9);
    }

    [Theory]
    [InlineData("FV-101_command", 0.5)] // comandada pelo mestre (holding register)
    [InlineData("nao-existe", 0.5)]
    [InlineData("FV-103_command", double.NaN)]
    public void Disturbance_OutsideTheHmiSection_OrNotFinite_IsRefused(string input, double value)
    {
        PlantInstance plant = Create();
        double[] before = [.. plant.GetState().Inputs.Select(i => i.Value)];

        Assert.False(plant.SetDisturbance(input, value));
        Assert.Equal(before, plant.GetState().Inputs.Select(i => i.Value));
    }

    [Fact]
    public void Alarms_FollowTheHmiSignalsAndTheWatchdog()
    {
        PlantInstance plant = Create();
        Assert.Equal(["LSLL-101", "LSHH-101", "PSH-101", "PSV-101_open", "watchdog"], plant.AlarmSignals);

        plant.Io.UpdateSettings(new IoSettings(WatchdogEnabled: true, WatchdogTimeoutSeconds: 0.1));
        using PlantSubscription subscription = plant.Subscribe();
        Step(plant, 20);

        PlantSnapshot snapshot = Latest(subscription);
        ActiveAlarmInfo watchdog = Assert.Single(snapshot.Alarms.Active);
        Assert.Equal("watchdog", watchdog.Signal);
        Assert.Equal(0.1, watchdog.SinceSeconds, 9); // dispara no 10º passo sem heartbeat
        long version = snapshot.Alarms.Version;

        // Watchdog desligado (o estado seguro corta o combustível e o nível pararia de cair) e bomba desligada:
        // o nível cai até a chave de nível muito baixo (0,30 m).
        plant.Io.UpdateSettings(new IoSettings());
        Assert.True(plant.Io.Image.TryWriteCoil(0, false));
        int steps = 0;
        while (plant.Io.Plant.GetOutput("LSLL-101") < 0.5 && steps < 200_000)
        {
            Step(plant, 100);
            steps += 100;
        }

        snapshot = Latest(subscription);
        Assert.Equal(["LSLL-101"], snapshot.Alarms.Active.Select(a => a.Signal));
        Assert.True(snapshot.Alarms.Version > version);
    }

    [Fact]
    public void Restart_GoesBackToTheInitialState_KeepsModbus_AndStartsANewRecording()
    {
        PlantInstance plant = Create(record: true);
        int port = plant.Modbus.Port;
        string firstRecording = plant.RecordingPath!;
        using PlantSubscription subscription = plant.Subscribe();
        Assert.True(plant.SetDisturbance("FV-103_command", 0.9));
        Step(plant, 1000);

        Assert.True(plant.Restart());

        PlantSnapshot snapshot = Latest(subscription);
        Assert.Equal(0, snapshot.StepCount);
        Assert.Equal(1, snapshot.Run);
        Assert.Equal(0.5, plant.Io.Plant.GetInput("FV-103_command"));
        Assert.Equal(port, plant.Modbus.Port);
        Assert.Equal(Path.Combine(_recordings, $"{plant.Id:N}-1.csv"), plant.RecordingPath);

        // O arquivo anterior foi fechado com os 1000 passos; o novo recomeça do cabeçalho.
        Assert.Contains("\n1000,10,", File.ReadAllText(firstRecording), StringComparison.Ordinal);
        Step(plant, 10);
        _manager.End(plant.Id);
        string[] lines = File.ReadAllLines(plant.RecordingPath!);
        Assert.Equal("#format=industrialsim-recording/1", lines[0]);
        Assert.StartsWith("1,0.01,", lines.First(l => char.IsDigit(l[0])), StringComparison.Ordinal);
    }

    /// <summary>O reinício dá o mesmo resultado que uma planta nova com os mesmos comandos (mesmo hash).</summary>
    [Fact]
    public void Restart_IsDeterministic()
    {
        PlantInstance plant = Create();
        PlantInstance fresh = Create();
        Step(plant, 700);
        Assert.True(plant.Restart());

        Step(plant, 300);
        Step(fresh, 300);

        Assert.Equal(fresh.Io.Plant.StateHash(), plant.Io.Plant.StateHash());
    }

    [Fact]
    public void EndedPlant_RefusesCommands_AndCompletesTheSubscriptions()
    {
        PlantInstance plant = Create();
        PlantSubscription subscription = plant.Subscribe();
        Step(plant, 5);

        _manager.End(plant.Id);

        PlantSnapshot last = Latest(subscription);
        Assert.Equal(InstanceStatus.Ended, last.Status);
        Assert.True(subscription.Reader.Completion.IsCompleted);
        Assert.False(plant.Restart());
        Assert.False(plant.SetDisturbance("FV-103_command", 0.7));
        Assert.True(plant.Subscribe().Reader.Completion.IsCompleted);
    }

    public void Dispose()
    {
        _manager.Dispose();
        _loop.Dispose();
        if (Directory.Exists(_recordings))
        {
            Directory.Delete(_recordings, recursive: true);
        }
    }

    private PlantInstance Create(bool record = false)
    {
        CreatePlantResult result = _manager.Create(new CreatePlantRequest("caldeira", Record: record));
        return result.Instance ?? throw new InvalidOperationException(result.Status.ToString());
    }

    private static void Step(PlantInstance plant, int count)
    {
        for (int i = 0; i < count; i++)
        {
            ((IRealTimeStepper)plant).Step();
        }
    }

    private static PlantSnapshot Latest(PlantSubscription subscription)
    {
        PlantSnapshot? latest = null;
        while (subscription.Reader.TryRead(out PlantSnapshot? snapshot))
        {
            latest = snapshot;
        }

        return latest ?? throw new InvalidOperationException("Nenhum estado publicado.");
    }
}
