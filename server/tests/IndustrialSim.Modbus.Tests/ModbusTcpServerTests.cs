using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using FluentModbus;
using IndustrialSim.IO;

namespace IndustrialSim.Modbus.Tests;

/// <summary>
/// O servidor Modbus TCP em loopback: interoperabilidade com um mestre independente (FluentModbus),
/// enquadramento, exceções sem perder a conexão e a retenção das conexões pelo pareamento.
/// </summary>
public sealed class ModbusTcpServerTests : IDisposable
{
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(300);
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Enumerable.Reverse(_disposables))
        {
            disposable.Dispose();
        }
    }

    private ModbusTcpServer Start(IoImage image, TestGate gate, ModbusServerOptions? options = null)
    {
        var server = new ModbusTcpServer(new ModbusDevice(image), gate, options);
        _disposables.Add(server);
        server.Start(new IPEndPoint(IPAddress.Loopback, 0));
        return server;
    }

    private ModbusTcpClient ConnectFluent(ModbusTcpServer server)
    {
        var client = new ModbusTcpClient { ReadTimeout = 5000, WriteTimeout = 5000 };
        _disposables.Add(client);
        client.Connect(server.LocalEndPoint!, ModbusEndianness.BigEndian);
        return client;
    }

    private async Task<RawModbusClient> ConnectRaw(ModbusTcpServer server, IPAddress? local = null)
    {
        RawModbusClient client = await RawModbusClient.ConnectAsync(server.LocalEndPoint!, local);
        _disposables.Add(client);
        return client;
    }

    /// <summary>Um mestre de outra implementação lê e escreve as quatro áreas da caldeira.</summary>
    [Fact]
    public void IndependentMaster_ReadsAndWritesTheCaldeiraImage()
    {
        PlantIo io = Caldeira.Create();
        ModbusTcpServer server = Start(io.Image, new TestGate());
        ModbusTcpClient client = ConnectFluent(server);

        client.WriteSingleRegister(1, Caldeira.WaterValveCommand, (short)13824);
        client.WriteMultipleRegisters(1, Caldeira.FuelValveCommand, new short[] { 6912 });
        client.WriteSingleCoil(1, Caldeira.PumpRun, false);

        IoSnapshot snapshot = io.Image.Snapshot();
        Assert.Equal(13824, snapshot.HoldingRegisters[Caldeira.WaterValveCommand]);
        Assert.Equal(6912, snapshot.HoldingRegisters[Caldeira.FuelValveCommand]);
        Assert.False(snapshot.Coils[Caldeira.PumpRun]);

        short[] levelAndPressure = client.ReadInputRegisters<short>(1, Caldeira.Level, 2).ToArray();
        Assert.Equal([(short)snapshot.InputRegisters[0], (short)snapshot.InputRegisters[1]], levelAndPressure);
        Assert.Equal(13824, levelAndPressure[0]); // 0,6 m em 0–1,2 m
        Assert.Equal((short)13824, client.ReadHoldingRegisters<short>(1, Caldeira.WaterValveCommand, 1)[0]);
        Assert.Equal(0x01, client.ReadDiscreteInputs(1, Caldeira.PumpRunning, 1)[0] & 0x01); // bomba em operação
        Assert.Equal(0x00, client.ReadCoils(1, Caldeira.PumpRun, 1)[0]);

        string author = IPAddress.Loopback.ToString();
        Assert.Equal(author, server.Device.Writes.Get(ModbusArea.HoldingRegister, Caldeira.WaterValveCommand)?.Author);
        Assert.Equal(author, server.Device.Writes.Get(ModbusArea.Coil, Caldeira.PumpRun)?.Author);
    }

    /// <summary>Critério 4: leitura fora do mapa devolve a exceção 02 e a conexão continua válida.</summary>
    [Fact]
    public async Task ReadOutsideTheMap_ReturnsException02_AndTheConnectionStaysUsable()
    {
        PlantIo io = Caldeira.Create();
        ModbusTcpServer server = Start(io.Image, new TestGate());

        // Mestre independente: a exceção chega como ModbusException, e a próxima leitura funciona.
        ModbusTcpClient fluent = ConnectFluent(server);
        ModbusException exception = Assert.Throws<ModbusException>(() => fluent.ReadInputRegisters<short>(1, 200, 1));
        Assert.Equal(FluentModbus.ModbusExceptionCode.IllegalDataAddress, exception.ExceptionCode);
        Assert.True(fluent.IsConnected);
        Assert.Equal(13824, fluent.ReadInputRegisters<short>(1, Caldeira.Level, 1)[0]);

        // Mesma verificação com bytes: resposta 0x84 02 e depois uma leitura normal na mesma conexão.
        RawModbusClient raw = await ConnectRaw(server);
        Assert.Equal([0x84, 0x02], await raw.RequestAsync(1, 0x04, 0x00, 0xC8, 0x00, 0x01));
        Assert.Equal([0x04, 0x02, 0x36, 0x00], await raw.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x01));
        Assert.All(server.Connections, c => Assert.Equal(2, c.Requests)); // as duas conexões continuam abertas
        Assert.Equal(2, server.Connections.Count);
    }

    [Fact]
    public async Task FramesSplitInPieces_OrSentTogether_AreAnswered()
    {
        var image = new IoImage(0, 0, 4, 0);
        Assert.True(image.TryWriteHoldingRegisters(0, [1, 2, 3, 4]));
        ModbusTcpServer server = Start(image, new TestGate());
        RawModbusClient client = await ConnectRaw(server);

        // Um byte por vez.
        byte[] frame = client.Frame(1, 0x03, 0x00, 0x02, 0x00, 0x01);
        foreach (byte b in frame)
        {
            await client.SendAsync(new[] { b });
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Equal([0x03, 0x02, 0x00, 0x03], (await client.ReceiveAsync(TimeSpan.FromSeconds(5)))?.Pdu);

        // Duas requisições no mesmo pacote: duas respostas, na ordem.
        byte[] first = client.Frame(1, 0x03, 0x00, 0x00, 0x00, 0x01);
        byte[] second = client.Frame(1, 0x03, 0x00, 0x03, 0x00, 0x01);
        await client.SendAsync(first.Concat(second).ToArray());
        Assert.Equal([0x03, 0x02, 0x00, 0x01], (await client.ReceiveAsync(TimeSpan.FromSeconds(5)))?.Pdu);
        Assert.Equal([0x03, 0x02, 0x00, 0x04], (await client.ReceiveAsync(TimeSpan.FromSeconds(5)))?.Pdu);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x01, 0x00, 0x06, 0x01 })] // protocolo 1
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x01 })] // sem PDU
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01 })] // PDU de 255 bytes
    public async Task InvalidHeader_ClosesTheConnection(byte[] header)
    {
        ModbusTcpServer server = Start(new IoImage(1, 1, 1, 1), new TestGate());
        RawModbusClient client = await ConnectRaw(server);

        await client.SendAsync(header);

        Assert.True(await client.WasClosedByServerAsync());
    }

    /// <summary>
    /// Pareamento pendente: a conexão fica aberta e as requisições são descartadas sem resposta; depois da
    /// autorização, só as requisições novas são atendidas.
    /// </summary>
    [Fact]
    public async Task PendingConnection_IsHeldWithoutAnswers_AndOldRequestsAreNotExecutedAfterAuthorization()
    {
        var image = new IoImage(0, 0, 2, 0);
        var gate = new TestGate(GateMode.Hold);
        ModbusTcpServer server = Start(image, gate);
        RawModbusClient client = await ConnectRaw(server);
        TestGate.HeldRequest held = await gate.NextHeldAsync();

        await client.SendAsync(client.Frame(1, 0x06, 0x00, 0x00, 0x00, 0x2A));
        Assert.Null(await client.ReceiveAsync(Silence));
        Assert.False(Assert.Single(server.Connections).Authorized);

        held.Allow();
        Assert.Equal([0x03, 0x02, 0x00, 0x00], await client.RequestAsync(1, 0x03, 0x00, 0x00, 0x00, 0x01));
        Assert.Equal(0, image.Snapshot().HoldingRegisters[0]);
        Assert.Empty(server.Device.Writes.Entries());
        Assert.True(Assert.Single(server.Connections).Authorized);
        Assert.Equal(1, gate.ActiveLeases);

        client.Dispose();
        await WaitUntil(() => server.Connections.Count == 0);
        Assert.Equal(0, gate.ActiveLeases);
    }

    /// <summary>
    /// Pendente sem nenhuma requisição: a leitura começada na abertura da conexão continua valendo depois da
    /// autorização, e a primeira requisição é respondida.
    /// </summary>
    [Fact]
    public async Task PendingConnectionWithoutRequests_IsAnsweredRightAfterAuthorization()
    {
        var gate = new TestGate(GateMode.Hold);
        ModbusTcpServer server = Start(new IoImage(0, 0, 1, 0), gate);
        RawModbusClient client = await ConnectRaw(server);
        TestGate.HeldRequest held = await gate.NextHeldAsync();
        await Task.Delay(Silence, TestContext.Current.CancellationToken);

        held.Allow();
        await WaitUntil(() => Assert.Single(server.Connections).Authorized);

        Assert.Equal([0x03, 0x02, 0x00, 0x00], await client.RequestAsync(1, 0x03, 0x00, 0x00, 0x00, 0x01));
    }

    /// <summary>
    /// Uma requisição mandada logo depois do aceite, antes de a decisão alcançar a conexão, é atendida (e não
    /// descartada como as de antes da autorização). Repetido para pegar as duas ordens de chegada.
    /// </summary>
    [Fact]
    public async Task ARequestSentRightAfterTheDecision_IsAnswered()
    {
        for (int i = 0; i < 20; i++)
        {
            var gate = new TestGate(GateMode.Hold);
            ModbusTcpServer server = Start(new IoImage(0, 0, 1, 0), gate);
            RawModbusClient client = await ConnectRaw(server);
            TestGate.HeldRequest held = await gate.NextHeldAsync();

            held.Allow();

            Assert.Equal([0x03, 0x02, 0x00, 0x00], await client.RequestAsync(1, 0x03, 0x00, 0x00, 0x00, 0x01));
        }
    }

    [Fact]
    public async Task RefusedConnection_IsClosed()
    {
        var gate = new TestGate(GateMode.Hold);
        ModbusTcpServer server = Start(new IoImage(1, 1, 1, 1), gate);
        RawModbusClient client = await ConnectRaw(server);

        (await gate.NextHeldAsync()).Refuse();

        Assert.True(await client.WasClosedByServerAsync());
        await WaitUntil(() => server.Connections.Count == 0);
    }

    /// <summary>Se o mestre desiste antes da decisão e depois ela vem com autorização, nada fica preso.</summary>
    [Fact]
    public async Task MasterLeavingWhilePending_DoesNotLeakTheAuthorization()
    {
        var gate = new TestGate(GateMode.Hold);
        ModbusTcpServer server = Start(new IoImage(1, 1, 1, 1), gate);
        RawModbusClient client = await ConnectRaw(server);
        TestGate.HeldRequest held = await gate.NextHeldAsync();

        client.Dispose();
        await WaitUntil(() => server.Connections.Count == 0);
        held.Allow();

        await WaitUntil(() => gate.ActiveLeases == 0);
    }

    /// <summary>
    /// O pareamento vê o IP de origem: uma conexão de 127.0.0.2 chega como 127.0.0.2, e fechar as conexões
    /// desse IP (retirada da autorização) não mexe nas de 127.0.0.1.
    /// </summary>
    [Fact]
    public async Task ConnectionsAreIdentifiedBySourceAddress_AndCanBeClosedPerAddress()
    {
        var gate = new TestGate();
        var image = new IoImage(0, 0, 1, 0);
        ModbusTcpServer server = Start(image, gate);
        RawModbusClient local = await ConnectRaw(server);
        RawModbusClient other = await ConnectRaw(server, IPAddress.Parse("127.0.0.2"));
        Assert.Equal([0x06, 0x00, 0x00, 0x00, 0x05], await other.RequestAsync(1, 0x06, 0x00, 0x00, 0x00, 0x05));

        Assert.Equal("127.0.0.2", server.Device.Writes.Get(ModbusArea.HoldingRegister, 0)?.Author);
        Assert.Contains(IPAddress.Parse("127.0.0.2"), gate.Addresses);

        Assert.Equal(1, server.CloseConnections(IPAddress.Parse("127.0.0.2")));

        Assert.True(await other.WasClosedByServerAsync());
        Assert.Equal([0x03, 0x02, 0x00, 0x05], await local.RequestAsync(1, 0x03, 0x00, 0x00, 0x00, 0x01));
        await WaitUntil(() => gate.ActiveLeases == 1);
    }

    [Fact]
    public async Task ConnectionsAboveTheLimit_AreClosedImmediately()
    {
        ModbusTcpServer server = Start(new IoImage(0, 0, 1, 0), new TestGate(), new ModbusServerOptions { MaxConnections = 2 });
        RawModbusClient first = await ConnectRaw(server);
        RawModbusClient second = await ConnectRaw(server);
        await WaitUntil(() => server.Connections.Count == 2);

        RawModbusClient third = await ConnectRaw(server);

        Assert.True(await third.WasClosedByServerAsync());
        Assert.Equal([0x03, 0x02, 0x00, 0x00], await first.RequestAsync(1, 0x03, 0x00, 0x00, 0x00, 0x01));
        Assert.Equal([0x03, 0x02, 0x00, 0x00], await second.RequestAsync(1, 0x03, 0x00, 0x00, 0x00, 0x01));
    }

    /// <summary>Encerrar o servidor fecha as conexões e libera a porta para outra planta.</summary>
    [Fact]
    public async Task Dispose_ClosesConnections_AndReleasesThePort()
    {
        var gate = new TestGate();
        ModbusTcpServer server = Start(new IoImage(0, 0, 1, 0), gate);
        IPEndPoint endPoint = server.LocalEndPoint!;
        RawModbusClient client = await ConnectRaw(server);
        await client.RequestAsync(1, 0x03, 0x00, 0x00, 0x00, 0x01);

        // Enquanto escuta, a porta não pode ser usada por outro servidor.
        using (var competitor = new ModbusTcpServer(new ModbusDevice(new IoImage(0, 0, 1, 0)), new TestGate()))
        {
            Assert.Throws<SocketException>(() => competitor.Start(endPoint));
        }

        server.Dispose();

        Assert.True(await client.WasClosedByServerAsync());
        await WaitUntil(() => gate.ActiveLeases == 0);
        using var next = new ModbusTcpServer(new ModbusDevice(new IoImage(0, 0, 1, 0)), new TestGate());
        next.Start(endPoint);
        Assert.Equal(endPoint, next.LocalEndPoint);
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
