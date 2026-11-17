using System.Net;
using System.Net.Sockets;
using IndustrialSim.Modbus.Tests;
using IndustrialSim.Sessions.Modbus;
using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Sessions.Tests;

/// <summary>
/// O Modbus das plantas em execução: uma porta por planta, liberada no fim, e o pareamento por IP (D07, D38).
/// </summary>
public sealed class PlantModbusTests : IDisposable
{
    private static readonly ScenarioCatalog Catalog = ProductScenarios.Catalog();
    private static readonly IPAddress Local = IPAddress.Loopback;
    private static readonly IPAddress Other = IPAddress.Parse("127.0.0.2");
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(300);

    // Leitura de LT-101 (input register 0).
    private static readonly byte[] ReadLevel = [0x04, 0x00, 0x00, 0x00, 0x01];

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

    private InstanceManager CreateManager(ModbusEndpointOptions? modbus = null)
    {
        var manager = new InstanceManager(Catalog, _loop, new InstanceManagerOptions { Modbus = modbus ?? new ModbusEndpointOptions() }, _time);
        _disposables.Add(manager);
        return manager;
    }

    private static PlantInstance Created(CreatePlantResult result)
    {
        Assert.Equal(CreatePlantStatus.Created, result.Status);
        return result.Instance!;
    }

    private async Task<RawModbusClient> Connect(PlantInstance plant, IPAddress from)
    {
        RawModbusClient client = await RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, plant.Modbus.Port), from);
        _disposables.Add(client);
        return client;
    }

    [Fact]
    public void PortAllocator_RotatesThroughTheRange_SkipsBusyPorts_AndReleases()
    {
        var ports = new PortAllocator(100, 103);

        Assert.Equal(100, ports.Allocate(_ => true));
        Assert.Equal(102, ports.Allocate(p => p != 101)); // 101 ocupada por outro programa
        ports.Release(100);
        Assert.Equal(103, ports.Allocate(_ => true)); // a busca continua depois da última entregue
        Assert.Equal(100, ports.Allocate(_ => true)); // e dá a volta
        Assert.Equal(101, ports.Allocate(_ => true));
        Assert.Null(ports.Allocate(_ => true));
        Assert.Equal(4, ports.InUseCount);
    }

    /// <summary>Cada planta escuta numa porta própria da faixa, e encerrar a planta fecha e devolve a porta.</summary>
    [Fact]
    public async Task EachPlantListensOnItsOwnPortInTheRange_ReleasedWhenItEnds()
    {
        InstanceManager manager = CreateManager();
        PlantInstance a = Created(manager.Create(new CreatePlantRequest("caldeira")));
        PlantInstance b = Created(manager.Create(new CreatePlantRequest("caldeira")));

        Assert.NotEqual(a.Modbus.Port, b.Modbus.Port);
        Assert.All([a, b], p => Assert.InRange(p.Modbus.Port, 15000, 15999));
        Assert.Equal(IPAddress.Loopback, a.Modbus.LocalEndPoint.Address);
        Assert.Equal(1, a.Modbus.UnitId);
        Assert.Equal(2, manager.ModbusPortsInUse);

        a.Modbus.Pairing.Authorize(Local);
        RawModbusClient master = await Connect(a, Local);
        Assert.Equal([0x04, 0x02, 0x36, 0x00], await master.RequestAsync(1, ReadLevel));

        int port = a.Modbus.Port;
        manager.End(a.Id);

        Assert.True(await master.WasClosedByServerAsync());
        Assert.Equal(1, manager.ModbusPortsInUse);
        await Assert.ThrowsAnyAsync<SocketException>(() => RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port)));
    }

    [Fact]
    public void WithoutAFreePort_ThePlantIsNotCreated_AndNoRecordingIsLeftBehind()
    {
        // Faixa de uma porta só, ocupada por outro programa.
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        int busy = ((IPEndPoint)blocker.LocalEndpoint).Port;
        string recordings = Path.Combine(Path.GetTempPath(), "industrialsim-ports-" + Guid.NewGuid().ToString("N"));
        var manager = new InstanceManager(
            Catalog,
            _loop,
            new InstanceManagerOptions { RecordingDirectory = recordings, Modbus = new ModbusEndpointOptions { PortRangeStart = busy, PortRangeEnd = busy } },
            _time);
        _disposables.Add(manager);

        CreatePlantResult result = manager.Create(new CreatePlantRequest("caldeira", Record: true));

        Assert.Equal(CreatePlantStatus.ModbusPortsExhausted, result.Status);
        Assert.Null(result.Instance);
        Assert.Equal(0, manager.Count);
        Assert.Equal(0, manager.ModbusPortsInUse);
        Assert.False(Directory.Exists(recordings) && Directory.EnumerateFiles(recordings).Any());
    }

    /// <summary>
    /// Critério 3: numa planta autorizada só para 127.0.0.1, uma conexão de 127.0.0.2 não é atendida, vira um
    /// pedido de pareamento e é fechada ao ser recusada; durante o tempo de recusa, nova conexão do mesmo IP é
    /// fechada na hora, sem novo pedido.
    /// </summary>
    [Fact]
    public async Task OnAPlantAuthorizedOnlyFor127001_AConnectionFrom127002IsRefused()
    {
        PlantInstance plant = Created(CreateManager().Create(new CreatePlantRequest("caldeira")));
        MasterPairing pairing = plant.Modbus.Pairing;
        Assert.True(pairing.Authorize(Local));

        RawModbusClient authorized = await Connect(plant, Local);
        Assert.Equal([0x04, 0x02, 0x36, 0x00], await authorized.RequestAsync(1, ReadLevel));

        RawModbusClient intruder = await Connect(plant, Other);
        await intruder.SendAsync(intruder.Frame(1, 0x06, 0x00, 0x00, 0x00, 0x00));
        Assert.Null(await intruder.ReceiveAsync(Silence));
        PairingRequestInfo request = Assert.Single(pairing.Pending);
        Assert.Equal(Other, request.Address);
        Assert.Equal(1, request.WaitingConnections);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromSeconds(60), request.ExpiresUtc);

        Assert.True(pairing.Refuse(request.Id));

        Assert.True(await intruder.WasClosedByServerAsync());
        Assert.Equal([Local], pairing.Authorized);
        Assert.Empty(pairing.Pending);
        Assert.Null(plant.Modbus.Device.Writes.Get(IO.ModbusArea.HoldingRegister, 0));

        // Logo depois da recusa: fechada na hora, sem pedido.
        RawModbusClient retry = await Connect(plant, Other);
        Assert.True(await retry.WasClosedByServerAsync());
        Assert.Empty(pairing.Pending);

        // Passado o tempo da recusa, uma nova conexão volta a pedir pareamento.
        _time.Advance(TimeSpan.FromSeconds(60));
        RawModbusClient later = await Connect(plant, Other);
        await WaitUntil(() => pairing.Pending.Count == 1);

        // E o mestre autorizado continua atendido o tempo todo.
        Assert.Equal([0x04, 0x02, 0x36, 0x00], await authorized.RequestAsync(1, ReadLevel));
        Assert.False(await later.WasClosedByServerAsync(Silence));
    }

    /// <summary>Sem resposta em 60 s, as conexões seguradas são fechadas; a próxima conexão abre outro pedido.</summary>
    [Fact]
    public async Task APairingRequestWithoutAnswerFor60s_ClosesTheConnection()
    {
        InstanceManager manager = CreateManager();
        PlantInstance plant = Created(manager.Create(new CreatePlantRequest("caldeira")));
        RawModbusClient master = await Connect(plant, Other);
        await WaitUntil(() => plant.Modbus.Pairing.Pending.Count == 1);
        Guid first = plant.Modbus.Pairing.Pending[0].Id;

        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(0, manager.ExpirePairingRequests());
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, manager.ExpirePairingRequests());

        Assert.True(await master.WasClosedByServerAsync());
        Assert.False(plant.Modbus.Pairing.Accept(first));

        RawModbusClient again = await Connect(plant, Other);
        await WaitUntil(() => plant.Modbus.Pairing.Pending.Count == 1);
        Assert.NotEqual(first, plant.Modbus.Pairing.Pending[0].Id);
    }

    /// <summary>
    /// Aceitar autoriza o IP: as conexões seguradas (duas do mesmo IP, um só pedido) passam a ser atendidas e
    /// contam como clientes da planta, que não expira; retirar a autorização as fecha.
    /// </summary>
    [Fact]
    public async Task AcceptingAuthorizesTheAddress_MastersKeepThePlantAlive_AndRevokingClosesThem()
    {
        InstanceManager manager = CreateManager();
        PlantInstance plant = Created(manager.Create(new CreatePlantRequest("caldeira")));
        MasterPairing pairing = plant.Modbus.Pairing;
        RawModbusClient first = await Connect(plant, Other);
        RawModbusClient second = await Connect(plant, Other);
        await WaitUntil(() => pairing.Pending.Count == 1 && pairing.Pending[0].WaitingConnections == 2);
        Assert.Equal(0, plant.ClientCount);

        Assert.True(pairing.Accept(pairing.Pending[0].Id));

        Assert.Equal([0x04, 0x02, 0x36, 0x00], await first.RequestAsync(1, ReadLevel));
        Assert.Equal([0x04, 0x02, 0x36, 0x00], await second.RequestAsync(1, ReadLevel));
        Assert.Equal([Other], pairing.Authorized);
        Assert.Equal(2, plant.ClientCount);
        Assert.All(plant.Modbus.Connections, c => Assert.True(c.Authorized));

        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, manager.ExpireIdle());

        Assert.True(pairing.Revoke(Other));

        Assert.True(await first.WasClosedByServerAsync());
        Assert.True(await second.WasClosedByServerAsync());
        await WaitUntil(() => plant.ClientCount == 0);
        Assert.Empty(pairing.Authorized);
        Assert.False(pairing.Revoke(Other));

        // Sem autorização, a próxima conexão volta a pedir pareamento.
        RawModbusClient third = await Connect(plant, Other);
        await WaitUntil(() => pairing.Pending.Count == 1);
    }

    [Fact]
    public async Task PendingRequests_AreLimitedPerPlant()
    {
        PlantInstance plant = Created(CreateManager(new ModbusEndpointOptions { MaxPendingRequests = 1 }).Create(new CreatePlantRequest("caldeira")));
        RawModbusClient first = await Connect(plant, Other);
        await WaitUntil(() => plant.Modbus.Pairing.Pending.Count == 1);

        RawModbusClient second = await Connect(plant, IPAddress.Parse("127.0.0.3"));

        Assert.True(await second.WasClosedByServerAsync());
        Assert.False(await first.WasClosedByServerAsync(Silence));
    }

    [Fact]
    public async Task UnitId_CanChangeWhileRunning()
    {
        PlantInstance plant = Created(CreateManager(new ModbusEndpointOptions { DefaultUnitId = 7 }).Create(new CreatePlantRequest("caldeira")));
        plant.Modbus.Pairing.Authorize(Local);
        RawModbusClient master = await Connect(plant, Local);

        Assert.Equal([0x04, 0x02, 0x36, 0x00], await master.RequestAsync(7, ReadLevel));
        Assert.Equal([0x84, 0x0B], await master.RequestAsync(1, ReadLevel));

        plant.Modbus.UnitId = 1;

        Assert.Equal([0x04, 0x02, 0x36, 0x00], await master.RequestAsync(1, ReadLevel));
    }

    /// <summary>Encerrar uma planta com pedido pendente fecha a conexão segurada e não deixa cliente preso.</summary>
    [Fact]
    public async Task EndingAPlant_ClosesPendingConnections()
    {
        InstanceManager manager = CreateManager();
        PlantInstance plant = Created(manager.Create(new CreatePlantRequest("caldeira")));
        RawModbusClient master = await Connect(plant, Other);
        await WaitUntil(() => plant.Modbus.Pairing.Pending.Count == 1);

        manager.End(plant.Id);

        Assert.True(await master.WasClosedByServerAsync());
        Assert.Equal(0, plant.ClientCount);
        Assert.Empty(plant.Modbus.Connections);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "A condição não foi atingida em 5 s.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
