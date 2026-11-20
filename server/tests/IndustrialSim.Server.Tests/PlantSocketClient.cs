using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IndustrialSim.Contracts;
using Microsoft.AspNetCore.TestHost;

namespace IndustrialSim.Server.Tests;

/// <summary>Cliente do WebSocket da planta sobre o servidor de teste.</summary>
internal sealed class PlantSocketClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly WebSocket _socket;

    private PlantSocketClient(WebSocket socket) => _socket = socket;

    public long BytesReceived { get; private set; }

    public string? LastText { get; private set; }

    public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;

    public static async Task<PlantSocketClient> ConnectAsync(SimulationFactory factory, Guid plantId)
    {
        WebSocketClient client = factory.Server.CreateWebSocketClient();
        WebSocket socket = await client.ConnectAsync(new Uri(factory.Server.BaseAddress, $"api/plants/{plantId}/ws"), Cancel);
        return new PlantSocketClient(socket);
    }

    public void ResetCounters() => BytesReceived = 0;

    public async Task<WelcomeMessage> HelloAsync()
    {
        await SendAsync(new HelloMessage(PlantSocketProtocol.Version));
        return Assert.IsType<WelcomeMessage>(await ReceiveAsync());
    }

    public Task SendAsync(ClientMessage message) =>
        _socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, Json), WebSocketMessageType.Text, true, Cancel);

    /// <summary>Próxima mensagem; nula quando o servidor fecha.</summary>
    public async Task<ServerMessage?> ReceiveAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        timeout.CancelAfter(Timeout);
        var buffer = new byte[1024 * 1024];
        int length = 0;
        while (true)
        {
            WebSocketReceiveResult result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (_socket.State == WebSocketState.CloseReceived)
                {
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, Cancel);
                }

                return null;
            }

            length += result.Count;
            if (result.EndOfMessage)
            {
                break;
            }
        }

        BytesReceived += length;
        LastText = Encoding.UTF8.GetString(buffer, 0, length);
        return JsonSerializer.Deserialize<ServerMessage>(buffer.AsSpan(0, length), Json);
    }

    public Task<StateMessage> NextStateAsync(Func<StateMessage, bool>? condition = null) => NextAsync(condition);

    public async Task<T> NextAsync<T>(Func<T, bool>? condition = null)
        where T : ServerMessage
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Timeout)
        {
            ServerMessage? message = await ReceiveAsync() ?? throw new InvalidOperationException($"Conexão fechada esperando {typeof(T).Name}.");
            if (message is T typed && (condition is null || condition(typed)))
            {
                return typed;
            }
        }

        throw new TimeoutException($"Nenhuma {typeof(T).Name} esperada em {Timeout.TotalSeconds} s.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
        }

        _socket.Dispose();
    }
}
