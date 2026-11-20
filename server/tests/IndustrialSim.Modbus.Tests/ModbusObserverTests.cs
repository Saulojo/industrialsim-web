using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using IndustrialSim.IO;

namespace IndustrialSim.Modbus.Tests;

/// <summary>Etapa 8: o servidor avisa o log de comunicação das conexões e das respostas de exceção.</summary>
public sealed class ModbusObserverTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Enumerable.Reverse(_disposables))
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task AuthorizedConnection_Exception_AndClose_AreReported()
    {
        var observer = new RecordingObserver();
        ModbusTcpServer server = Start(new TestGate(GateMode.Allow), observer);
        RawModbusClient master = await RawModbusClient.ConnectAsync(server.LocalEndPoint!);
        _disposables.Add(master);
        await WaitUntil(() => observer.Events.Contains("authorized"));

        Assert.Equal([0x83, 0x02], await master.RequestAsync(1, 0x03, 0x01, 0x2C, 0x00, 0x01)); // holding register 300
        Assert.Equal([0x81, 0x0B], await master.RequestAsync(9, 0x01, 0x00, 0x00, 0x00, 0x01)); // Unit ID errado
        Assert.Equal([0xAB, 0x01], await master.RequestAsync(1, 0x2B, 0x0E, 0x01, 0x00)); // função sem endereço
        master.Dispose();
        await WaitUntil(() => observer.Events.Any(e => e.StartsWith("closed", StringComparison.Ordinal)));

        Assert.Equal(["authorized", "exception 3 2 300", "exception 1 11 0", "exception 43 1 -", "closed 3"], observer.Events);
    }

    [Fact]
    public async Task RefusedConnections_AndTheConnectionLimit_AreReported()
    {
        var observer = new RecordingObserver();
        ModbusTcpServer refusing = Start(new TestGate(GateMode.Refuse), observer);
        using (RawModbusClient refused = await RawModbusClient.ConnectAsync(refusing.LocalEndPoint!))
        {
            Assert.True(await refused.WasClosedByServerAsync());
        }

        var limited = new RecordingObserver();
        ModbusTcpServer full = Start(new TestGate(GateMode.Hold), limited, new ModbusServerOptions { MaxConnections = 1 });
        RawModbusClient first = await RawModbusClient.ConnectAsync(full.LocalEndPoint!);
        _disposables.Add(first);
        await WaitUntil(() => full.Connections.Count == 1);
        using RawModbusClient second = await RawModbusClient.ConnectAsync(full.LocalEndPoint!);
        Assert.True(await second.WasClosedByServerAsync());

        Assert.Equal(["refused"], observer.Events);
        Assert.Equal(["limit"], limited.Events);
    }

    [Theory]
    [InlineData(new byte[] { 0x03, 0x00, 0x0A, 0x00, 0x01 }, 10)]
    [InlineData(new byte[] { 0x10, 0x01, 0x00, 0x00, 0x01, 0x02, 0x00, 0x00 }, 256)]
    [InlineData(new byte[] { 0x05, 0x00 }, null)]
    [InlineData(new byte[] { 0x08, 0x00, 0x00 }, null)]
    public void StartAddress_IsReadFromTheRequest_WhenTheFunctionHasOne(byte[] pdu, int? expected) =>
        Assert.Equal(expected, ModbusTcpServer.StartAddress(pdu));

    private ModbusTcpServer Start(TestGate gate, RecordingObserver observer, ModbusServerOptions? options = null)
    {
        var server = new ModbusTcpServer(new ModbusDevice(new IoImage(1, 6, 11, 110)), gate, options, observer: observer);
        _disposables.Add(server);
        server.Start(new IPEndPoint(IPAddress.Loopback, 0));
        return server;
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

    private sealed class RecordingObserver : IModbusServerObserver
    {
        private readonly ConcurrentQueue<string> _events = new();

        public string[] Events => [.. _events];

        public void ConnectionAuthorized(long connectionId, IPEndPoint remote) => _events.Enqueue("authorized");

        public void ConnectionRefused(long connectionId, IPEndPoint remote) => _events.Enqueue("refused");

        public void ConnectionClosed(long connectionId, IPEndPoint remote, long requests) => _events.Enqueue($"closed {requests}");

        public void ConnectionLimitReached(IPEndPoint remote) => _events.Enqueue("limit");

        public void ExceptionResponse(long connectionId, IPEndPoint remote, byte function, byte exceptionCode, int? address) =>
            _events.Enqueue($"exception {function} {exceptionCode} {(address is int a ? a.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}");
    }
}
