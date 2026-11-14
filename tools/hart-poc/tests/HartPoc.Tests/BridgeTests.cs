using System.Net.WebSockets;
using HartPoc.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HartPoc.Tests;

public class BridgeTests
{
    private static readonly byte[] UniqueAddress = [0x3E, 0x01, 0x0E, 0x2E, 0x02];

    // Sessão típica de um mestre: identifica, lê, escreve faixa e amortecimento e relê.
    private static byte[] MasterStream()
    {
        var damping = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(damping, 1.5f);
        var range = new byte[9];
        range[0] = PressureUnits.Kilopascal;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(range.AsSpan(1), 40f);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(range.AsSpan(5), 10f);

        HartFrame[] requests =
        [
            HartFrame.ShortRequest(true, 0, 0),
            HartFrame.LongRequest(true, UniqueAddress, 13),
            HartFrame.LongRequest(true, UniqueAddress, 15),
            HartFrame.LongRequest(true, UniqueAddress, 3),
            HartFrame.LongRequest(true, UniqueAddress, 35, range),
            HartFrame.LongRequest(true, UniqueAddress, 34, damping),
            HartFrame.LongRequest(true, UniqueAddress, 15),
            HartFrame.LongRequest(true, UniqueAddress, 3),
        ];
        return [.. requests.SelectMany(request => request.Encode(5))];
    }

    private static byte[] Run(byte[] stream, Func<int, int> chunkSize)
    {
        var session = new HartBridgeSession(
            new HartDevice(new HartDeviceIdentity(), _ => 25.0), new BridgeCounters(), _ => { });
        var output = new List<byte>();
        long now = 0;
        for (int offset = 0, i = 0; offset < stream.Length; i++)
        {
            var size = Math.Min(chunkSize(i), stream.Length - offset);
            output.AddRange(session.OnBytes(stream.AsSpan(offset, size), now));
            offset += size;
            now += 5;
        }

        return [.. output];
    }

    [Fact]
    public void Session_SameStreamInChunksOfAnySize_GivesTheSameResponses()
    {
        var stream = MasterStream();
        var whole = Run(stream, _ => stream.Length);

        var responses = new HartFrameReader().Feed(whole, 0);
        Assert.Equal(8, responses.Count);
        Assert.All(responses, response => Assert.True(response.CheckByteOk));
        Assert.All(responses, response => Assert.Equal(0, response.Frame.Data[0]));

        var random = new Random(7);
        Assert.Equal(whole, Run(stream, _ => 1));
        Assert.Equal(whole, Run(stream, _ => 7));
        Assert.Equal(whole, Run(stream, _ => 1 + random.Next(40)));
    }

    [Fact]
    public void Session_CountsFramesBytesAndGarbage()
    {
        var counters = new BridgeCounters();
        var session = new HartBridgeSession(new HartDevice(new HartDeviceIdentity(), _ => 25.0), counters, _ => { });
        var corrupted = HartFrame.ShortRequest(true, 0, 0).Encode(5);
        corrupted[^1] ^= 0xFF;
        byte[] stream = [0x00, 0x55, .. HartFrame.ShortRequest(true, 0, 0).Encode(5), .. corrupted];

        var output = session.OnBytes(stream, 0);

        Assert.Equal(stream.Length, counters.BytesReceived);
        Assert.Equal(output.Length, counters.BytesSent);
        Assert.Equal(2, counters.FramesReceived);
        Assert.Equal(2, counters.FramesAnswered);
        Assert.Equal(1, counters.CheckByteErrors);
        Assert.Equal(2, counters.DiscardedBytes);
    }

    [Fact]
    public async Task Server_HartSocket_AnswersARequestSplitInTwoMessages()
    {
        await using var app = await StartAsync();
        using var socket = await ConnectAsync(app, "/ws/hart");
        var request = HartFrame.ShortRequest(true, 0, 0).Encode(5);
        var cancellation = TestContext.Current.CancellationToken;

        await socket.SendAsync(request.AsMemory(0, 6), WebSocketMessageType.Binary, true, cancellation);
        await socket.SendAsync(request.AsMemory(6), WebSocketMessageType.Binary, true, cancellation);
        var buffer = new byte[256];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellation);

        var response = Assert.Single(new HartFrameReader().Feed(buffer.AsSpan(0, result.Count), 0));
        Assert.True(response.CheckByteOk);
        Assert.Equal(HartFrameType.Response, response.Frame.Type);
        Assert.Equal([254, 0x3E, 0x01], response.Frame.Data[2..5]);
    }

    [Fact]
    public async Task Server_EchoSocket_ReturnsTheSameBytes()
    {
        await using var app = await StartAsync();
        using var socket = await ConnectAsync(app, "/ws/echo");
        byte[] sent = [0x00, 0xFF, 0x02, 0x80, 0x7F, 0x13];
        var cancellation = TestContext.Current.CancellationToken;

        await socket.SendAsync(sent.AsMemory(), WebSocketMessageType.Binary, true, cancellation);
        var buffer = new byte[64];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellation);

        Assert.Equal(sent, buffer[..result.Count]);
    }

    [Fact]
    public async Task Server_ServesTheBridgePageAndTheStatus()
    {
        await using var app = await StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(Address(app)) };
        var cancellation = TestContext.Current.CancellationToken;

        var page = await http.GetStringAsync("/", cancellation);
        var status = await http.GetStringAsync("/api/status", cancellation);

        Assert.Contains("Conectar porta serial", page, StringComparison.Ordinal);
        Assert.Contains("\"hartRevision\":7", status, StringComparison.Ordinal);
        Assert.Contains("\"tag\":\"PT-101\"", status, StringComparison.Ordinal);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var logs = Path.Combine(Path.GetTempPath(), "hart-poc-tests", Guid.NewGuid().ToString("N"));
        var app = HartPocApp.Build(["--urls", "http://127.0.0.1:0", $"--Hart:LogDirectory={logs}"]);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static string Address(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    private static async Task<ClientWebSocket> ConnectAsync(WebApplication app, string path)
    {
        var socket = new ClientWebSocket();
        var uri = new Uri(Address(app).Replace("http://", "ws://", StringComparison.Ordinal) + path);
        await socket.ConnectAsync(uri, TestContext.Current.CancellationToken);
        return socket;
    }
}
