using System.Net;
using System.Runtime.CompilerServices;
using IndustrialSim.IO;
using IndustrialSim.Modbus.Tests;
using IndustrialSim.Sessions.Modbus;
using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Sessions.Tests;

/// <summary>
/// Etapa 8: configurações da planta, pausa, valores forçados, mestres permitidos e log de comunicação.
/// </summary>
public sealed class PlantCommunicationTests : IDisposable
{
    private static readonly ScenarioCatalog Catalog = ProductScenarios.Catalog();
    private static readonly IPAddress Local = IPAddress.Loopback;
    private static readonly IPAddress Other = IPAddress.Parse("127.0.0.2");

    private readonly ManualTimeProvider _time = new();
    private readonly RealTimeLoop _loop = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Enumerable.Reverse(_disposables))
        {
            disposable.Dispose();
        }

        _loop.Dispose();
    }

    /// <summary>
    /// Critério 5: pausar congela a planta (passo, tempo e hash do estado não mudam, por mais ciclos que o laço
    /// dê) e retomar continua do mesmo estado: depois de retomar, a planta é idêntica, bit a bit, a uma planta
    /// igual que nunca foi pausada, no mesmo passo.
    /// </summary>
    [Fact]
    public void Pause_FreezesThePlant_AndResume_ContinuesFromTheSameState()
    {
        InstanceManager manager = CreateManager();
        PlantInstance paused = Create(manager);
        PlantInstance reference = Create(manager);
        Steps(paused, 300);
        Steps(reference, 300);

        Assert.True(paused.Pause("10.0.0.5"));
        Assert.Equal(InstanceStatus.Paused, paused.Status);
        ulong hashAtPause = paused.Io.Plant.StateHash();
        long stepAtPause = paused.GetState().StepCount;

        Steps(paused, 1000); // dez segundos de ciclos do laço
        Assert.Equal(stepAtPause, paused.GetState().StepCount);
        Assert.Equal(hashAtPause, paused.Io.Plant.StateHash());
        Assert.Equal(reference.Io.Plant.StateHash(), hashAtPause);

        Assert.False(paused.Pause(), "Pausar uma planta já pausada não deveria fazer nada.");
        Assert.True(paused.Resume("10.0.0.5"));
        Assert.Equal(InstanceStatus.Running, paused.Status);
        Assert.Equal(hashAtPause, paused.Io.Plant.StateHash());

        Steps(paused, 500);
        Steps(reference, 500);
        Assert.Equal(reference.GetState().StepCount, paused.GetState().StepCount);
        Assert.Equal(reference.Io.Plant.StateHash(), paused.Io.Plant.StateHash());

        Assert.Equal(
            [(PlantEventKinds.Paused, "300"), (PlantEventKinds.Resumed, "300")],
            paused.Events.All().Select(e => (e.Kind, Arg(e, "step"))));
        Assert.All(paused.Events.All(), e => Assert.Equal("10.0.0.5", Arg(e, "author")));
    }

    /// <summary>O watchdog conta tempo simulado (D32): uma pausa mais longa que o tempo limite não o dispara.</summary>
    [Fact]
    public void APauseLongerThanTheWatchdogTimeout_DoesNotTripIt()
    {
        PlantInstance plant = Create(CreateManager());
        Assert.Equal(SettingsUpdateResult.Applied, plant.UpdateSettings(new PlantSettingsUpdate(WatchdogEnabled: true, WatchdogTimeoutSeconds: 0.5)));
        Steps(plant, 10);
        Assert.True(plant.Pause());
        Steps(plant, 200);
        Assert.False(plant.Io.WatchdogTripped);
        Assert.True(plant.Resume());

        Steps(plant, 60);
        Assert.True(plant.Io.WatchdogTripped, "Sem heartbeat, o watchdog deveria disparar depois de 0,5 s simulados.");
    }

    /// <summary>Pausada, a planta continua publicando o estado (com a situação "pausada") para a IHM e o monitor.</summary>
    [Fact]
    public async Task APausedPlant_KeepsPublishingItsStateToSubscribers()
    {
        PlantInstance plant = Create(CreateManager());
        using PlantSubscription subscription = plant.Subscribe();
        await subscription.Reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(plant.Pause());
        PlantSnapshot afterPause = await subscription.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstanceStatus.Paused, afterPause.Status);

        Steps(plant, 5);
        PlantSnapshot tick = await subscription.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstanceStatus.Paused, tick.Status);
        Assert.Equal(afterPause.StepCount, tick.StepCount);
    }

    /// <summary>
    /// Critério 6: cada configuração alterada gera exatamente uma linha no log, com o valor antigo, o novo e o
    /// autor; repetir o mesmo valor não gera linha.
    /// </summary>
    [Theory]
    [MemberData(nameof(EachSetting))]
    public void EverySettingChange_IsLoggedOnce_WithOldAndNewValuesAndAuthor(string setting, PlantSettingsUpdate update, string oldValue, string newValue)
    {
        PlantInstance plant = Create(CreateManager());

        Assert.Equal(SettingsUpdateResult.Applied, plant.UpdateSettings(update, "192.168.0.7"));
        Assert.Equal(SettingsUpdateResult.Applied, plant.UpdateSettings(update, "192.168.0.7"));

        PlantEvent entry = Assert.Single(plant.Events.All());
        Assert.Equal(PlantEventKinds.SettingChanged, entry.Kind);
        Assert.Equal(
            [new("setting", setting), new("old", oldValue), new("new", newValue), new("author", "192.168.0.7")],
            entry.Arguments);
    }

    public static TheoryData<string, PlantSettingsUpdate, string, string> EachSetting() => new()
    {
        { PlantSettingNames.Format, new PlantSettingsUpdate(Format: AnalogFormat.Float32), "counts27648", "float32" },
        { PlantSettingNames.WordOrder, new PlantSettingsUpdate(WordOrder: WordOrder.LowWordFirst), "highWordFirst", "lowWordFirst" },
        { PlantSettingNames.WatchdogEnabled, new PlantSettingsUpdate(WatchdogEnabled: true), "false", "true" },
        { PlantSettingNames.WatchdogTimeout, new PlantSettingsUpdate(WatchdogTimeoutSeconds: 5.0), "2", "5" },
        { PlantSettingNames.DiagnosticsEnabled, new PlantSettingsUpdate(DiagnosticsEnabled: false), "true", "false" },
        { PlantSettingNames.UnitId, new PlantSettingsUpdate(UnitId: 17), "1", "17" },
        { PlantSettingNames.MasterPolicy, new PlantSettingsUpdate(MasterPolicy: MasterPolicy.Single), "multiple", "single" },
    };

    /// <summary>Várias configurações numa mudança só: uma linha por configuração, na ordem da tela.</summary>
    [Fact]
    public void ChangingSeveralSettingsAtOnce_LogsOneLinePerSetting()
    {
        PlantInstance plant = Create(CreateManager());

        plant.UpdateSettings(new PlantSettingsUpdate(Format: AnalogFormat.Counts10000, UnitId: 3, DiagnosticsEnabled: true), "ihm");

        Assert.Equal(
            [PlantSettingNames.Format, PlantSettingNames.UnitId],
            plant.Events.All().Select(e => Arg(e, "setting")));
        Assert.Equal(new PlantSettings(new IoSettings(AnalogFormat.Counts10000), 3, MasterPolicy.Multiple), plant.Settings);
    }

    /// <summary>Um valor inválido recusa a mudança inteira: nada muda e nada vai para o log.</summary>
    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public void AnInvalidValue_RefusesTheWholeUpdate(PlantSettingsUpdate update)
    {
        PlantInstance plant = Create(CreateManager());
        PlantSettings before = plant.Settings;

        Assert.Equal(SettingsUpdateResult.Invalid, plant.UpdateSettings(update));

        Assert.Equal(before, plant.Settings);
        Assert.Empty(plant.Events.All());
    }

    public static TheoryData<PlantSettingsUpdate> InvalidUpdates() =>
    [
        new PlantSettingsUpdate(UnitId: 0, Format: AnalogFormat.Float32),
        new PlantSettingsUpdate(WatchdogTimeoutSeconds: 0.0, Format: AnalogFormat.Float32),
        new PlantSettingsUpdate(WatchdogTimeoutSeconds: double.NaN),
        new PlantSettingsUpdate(WatchdogTimeoutSeconds: 601.0),
        new PlantSettingsUpdate(Format: (AnalogFormat)99),
        new PlantSettingsUpdate(MasterPolicy: (MasterPolicy)7),
    ];

    /// <summary>
    /// Critério 1, na planta: trocar o formato altera o que o mestre lê no passo seguinte, sem reiniciar; trocar o
    /// Unit ID vale na requisição seguinte (o Unit ID antigo passa a receber a exceção 0x0B).
    /// </summary>
    [Fact]
    public async Task FormatAndUnitIdChanges_ApplyToTheMasterAtTheNextStep_WithoutRestarting()
    {
        PlantInstance plant = Create(CreateManager());
        plant.Modbus.Pairing.Authorize(Local);
        RawModbusClient master = await Connect(plant, Local);
        Steps(plant, 100);
        double level = Output(plant, "LT-101");
        Assert.InRange(await ReadRegister(master, 1, 0) - (27648.0 * level / 1.2), -1.0, 1.0);

        plant.UpdateSettings(new PlantSettingsUpdate(Format: AnalogFormat.Counts10000));
        Assert.InRange(await ReadRegister(master, 1, 0) - (27648.0 * level / 1.2), -1.0, 1.0); // ainda no passo atual
        Steps(plant, 1);
        level = Output(plant, "LT-101");
        Assert.InRange(await ReadRegister(master, 1, 0) - (10000.0 * level / 1.2), -1.0, 1.0);
        Assert.Equal(0, plant.Run);

        plant.UpdateSettings(new PlantSettingsUpdate(UnitId: 5));
        Assert.Equal([0x84, 0x0B], await master.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x01));
        Assert.InRange(await ReadRegister(master, 5, 0) - (10000.0 * level / 1.2), -1.0, 1.0);
    }

    /// <summary>
    /// Critério 3, na planta: um valor forçado vale para o mestre (com a marca de forçado na imagem) e deixa de
    /// valer ao ser liberado; as duas ações vão para o log. Com a planta pausada, o efeito é imediato.
    /// </summary>
    [Fact]
    public async Task ForcingAndReleasing_ChangeWhatTheMasterReads_AndAreLogged()
    {
        PlantInstance plant = Create(CreateManager());
        plant.Modbus.Pairing.Authorize(Local);
        RawModbusClient master = await Connect(plant, Local);
        Steps(plant, 10);
        Assert.True(plant.Pause());

        Assert.True(plant.Force(ModbusArea.InputRegister, "LT-101", 0.9, "ihm"));
        Assert.Equal(20736, await ReadRegister(master, 1, 0)); // 0,9 m de 1,2 m = 75 % de 27648
        Assert.True(plant.Io.Image.Snapshot().ForcedInputRegisters[0]);
        Assert.True(plant.Force(ModbusArea.DiscreteInput, "LSHH-101", 1.0, "ihm"));
        Assert.Equal(
            [new ForcedInput(ModbusArea.DiscreteInput, 2, "LSHH-101", 1.0), new ForcedInput(ModbusArea.InputRegister, 0, "LT-101", 0.9)],
            plant.Io.GetForced());

        Assert.True(plant.Release(ModbusArea.InputRegister, "LT-101", "ihm"));
        Assert.InRange(await ReadRegister(master, 1, 0) - (27648.0 * Output(plant, "LT-101") / 1.2), -1.0, 1.0);
        Assert.False(plant.Io.Image.Snapshot().ForcedInputRegisters[0]);

        Assert.False(plant.Force(ModbusArea.InputRegister, "não-existe", 1.0));
        Assert.False(plant.Force(ModbusArea.Coil, "P-101_run", 1.0));
        Assert.False(plant.Force(ModbusArea.InputRegister, "LT-101", double.PositiveInfinity));

        PlantEvent[] actions = [.. plant.Events.All().Where(e => e.Kind is PlantEventKinds.Paused or PlantEventKinds.InputForced or PlantEventKinds.InputReleased)];
        Assert.Equal(
            [PlantEventKinds.Paused, PlantEventKinds.InputForced, PlantEventKinds.InputForced, PlantEventKinds.InputReleased],
            actions.Select(e => e.Kind));
        PlantEvent forced = actions[1];
        Assert.Equal(
            [new("area", "input-register"), new("signal", "LT-101"), new("value", "0.9"), new("author", "ihm")],
            forced.Arguments);
    }

    /// <summary>
    /// D42: com "um mestre por vez", aceitar outro IP retira a autorização do anterior e fecha as conexões dele;
    /// pedir esse modo com dois IPs autorizados é recusado.
    /// </summary>
    [Fact]
    public async Task SingleMasterPolicy_ReplacesThePreviousMaster_AndIsRefusedWithTwoAuthorized()
    {
        PlantInstance plant = Create(CreateManager());
        MasterPairing pairing = plant.Modbus.Pairing;
        pairing.Authorize(Local);
        pairing.Authorize(Other);
        Assert.Equal(SettingsUpdateResult.SeveralMastersAuthorized, plant.UpdateSettings(new PlantSettingsUpdate(MasterPolicy: MasterPolicy.Single, UnitId: 9)));
        Assert.Equal(MasterPolicy.Multiple, pairing.Policy);
        Assert.Equal(1, plant.Modbus.UnitId);

        Assert.True(pairing.Revoke(Other, "ihm"));
        Assert.Equal(SettingsUpdateResult.Applied, plant.UpdateSettings(new PlantSettingsUpdate(MasterPolicy: MasterPolicy.Single)));
        RawModbusClient first = await Connect(plant, Local);
        Assert.Equal(4, (await first.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x01)).Length);

        RawModbusClient second = await Connect(plant, Other);
        await WaitUntil(() => pairing.Pending.Count == 1);
        Assert.True(pairing.Accept(pairing.Pending[0].Id, "ihm"));

        Assert.True(await first.WasClosedByServerAsync(), "A conexão do mestre anterior deveria ter sido fechada.");
        Assert.Equal([Other], pairing.Authorized);
        Assert.Equal(4, (await second.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x01)).Length);

        PlantEvent replaced = Assert.Single(plant.Events.All(), e => e.Kind == PlantEventKinds.MasterReplaced);
        Assert.Equal("127.0.0.1", Arg(replaced, "address"));
        Assert.Equal("127.0.0.2", Arg(replaced, "replacement"));
    }

    /// <summary>
    /// O log recebe as conexões, os pareamentos e as exceções Modbus com o endereço pedido; exceções iguais
    /// seguidas viram uma linha só, com a contagem.
    /// </summary>
    [Fact]
    public async Task ConnectionsPairingsAndExceptions_GoToTheLog()
    {
        PlantInstance plant = Create(CreateManager());
        RawModbusClient master = await Connect(plant, Local);
        await WaitUntil(() => plant.Modbus.Pairing.Pending.Count == 1);
        plant.Modbus.Pairing.Accept(plant.Modbus.Pairing.Pending[0].Id, "ihm");
        await WaitUntil(() => plant.Events.All().Any(e => e.Kind == PlantEventKinds.ConnectionAuthorized));

        // Três leituras do input register 200, que não existe: exceção 02, endereço ilegal.
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal([0x84, 0x02], await master.RequestAsync(1, 0x04, 0x00, 200, 0x00, 0x01));
        }

        master.Dispose();
        await WaitUntil(() => plant.Events.All().Any(e => e.Kind == PlantEventKinds.ConnectionClosed));

        IReadOnlyList<PlantEvent> events = plant.Events.All();
        Assert.Equal(
            [PlantEventKinds.PairingRequested, PlantEventKinds.PairingAccepted, PlantEventKinds.ConnectionAuthorized, PlantEventKinds.ModbusException, PlantEventKinds.ConnectionClosed],
            events.Select(e => e.Kind));
        PlantEvent exception = events[3];
        Assert.Equal(3, exception.Count);
        Assert.Equal([new("address", "127.0.0.1"), new("function", "4"), new("code", "2"), new("register", "200")], exception.Arguments);
        Assert.Equal("3", Arg(events[4], "requests"));
    }

    /// <summary>A taxa de requisições é medida entre duas amostras do gerenciador.</summary>
    [Fact]
    public async Task RequestRate_IsMeasuredBetweenSamples()
    {
        PlantInstance plant = Create(CreateManager());
        plant.Modbus.Pairing.Authorize(Local);
        RawModbusClient master = await Connect(plant, Local);
        plant.Modbus.SampleRequestRate();

        for (int i = 0; i < 20; i++)
        {
            await master.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x01);
        }

        _time.Advance(TimeSpan.FromSeconds(2));
        plant.Modbus.SampleRequestRate();
        Assert.Equal(10.0, plant.Modbus.RequestsPerSecond, 6);
    }

    private InstanceManager CreateManager()
    {
        var manager = new InstanceManager(Catalog, _loop, new InstanceManagerOptions(), _time);
        _disposables.Add(manager);
        return manager;
    }

    private static PlantInstance Create(InstanceManager manager)
    {
        CreatePlantResult result = manager.Create(new CreatePlantRequest("caldeira"));
        Assert.Equal(CreatePlantStatus.Created, result.Status);
        return result.Instance!;
    }

    private async Task<RawModbusClient> Connect(PlantInstance plant, IPAddress from)
    {
        RawModbusClient client = await RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, plant.Modbus.Port), from);
        _disposables.Add(client);
        return client;
    }

    private static void Steps(PlantInstance plant, int count)
    {
        var stepper = (IRealTimeStepper)plant;
        for (int i = 0; i < count; i++)
        {
            stepper.Step();
        }
    }

    private static double Output(PlantInstance plant, string name) => plant.GetState().Outputs.Single(s => s.Name == name).Value;

    private static string? Arg(PlantEvent entry, string name) =>
        entry.Arguments.Where(a => a.Name == name).Select(a => a.Value).FirstOrDefault();

    private static async Task<short> ReadRegister(RawModbusClient master, byte unitId, byte address)
    {
        byte[] response = await master.RequestAsync(unitId, 0x04, 0x00, address, 0x00, 0x01);
        Assert.Equal(4, response.Length);
        return (short)((response[2] << 8) | response[3]);
    }

    private static async Task WaitUntil(Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Não foi atingido em 5 s: {expression}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
