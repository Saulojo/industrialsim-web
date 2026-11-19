using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IndustrialSim.Contracts;
using IndustrialSim.Modbus.Tests;
using IndustrialSim.Sessions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// Etapa 7: WebSocket da planta com o servidor inteiro em memória e o laço de tempo real rodando: versão do
/// protocolo, estado a 20 Hz, banda, distúrbio, reinício, pareamento e encerramento.
/// </summary>
public class PlantSocketTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hello_GetsWelcome_ThenStatesAt20Hz_WithinTheBandwidthBudget()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory);
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);

        WelcomeMessage welcome = await socket.HelloAsync();
        Assert.Equal(PlantSocketProtocol.Version, welcome.ProtocolVersion);
        Assert.Equal(plant.Id, welcome.Plant.Id);
        Assert.Equal(plant.ModbusPort, welcome.Plant.ModbusPort);
        Assert.Equal(0.05, welcome.StatePeriodSeconds, 12);
        Assert.Null(welcome.ServerAddress);
        Assert.Equal(["P-101_run", "FV-101_command", "FV-102_command", "FV-103_command"], welcome.Inputs.Select(i => i.Name));
        Assert.Equal(new SignalInfo("LT-101", SignalKinds.Analog, "m", 1.0, 0.0, 1.2, new ModbusPointInfo(ModbusAreas.InputRegister, 0, 30001)), welcome.Outputs[0]);
        Assert.Equal(new SignalInfo("PT-101", SignalKinds.Analog, "bar", 1e-5, 0.0, 1.6e6, new ModbusPointInfo(ModbusAreas.InputRegister, 1, 30002)), welcome.Outputs[1]);
        Assert.Equal(new ModbusPointInfo(ModbusAreas.InputRegister, 108, 30109), welcome.Outputs.Single(o => o.Name == "FV-103_position").Modbus);
        Assert.Equal(new ModbusPointInfo(ModbusAreas.Coil, 0, 1), welcome.Inputs[0].Modbus);
        Assert.Equal(SignalKinds.Digital, welcome.Outputs.Single(o => o.Name == "LSLL-101").Kind);
        Assert.Equal([new DisturbanceInfo("FV-103_command", 0.0, 1.0, "%", 100.0)], welcome.Disturbances);
        Assert.Equal(["LSLL-101", "LSHH-101", "PSH-101", "PSV-101_open", AlarmSignals.Watchdog], welcome.Alarms);

        // 5 s de mensagens: estados a cada 5 passos, um alarme e um Modbus no início.
        socket.ResetCounters();
        var watch = Stopwatch.StartNew();
        var states = new List<StateMessage>();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (await socket.ReceiveAsync() is StateMessage state)
            {
                states.Add(state);
            }
        }

        double seconds = watch.Elapsed.TotalSeconds;
        double rate = states.Count / seconds;
        double kbitPerSecond = socket.BytesReceived * 8 / 1000.0 / seconds;
        double simulated = states[^1].Time - states[0].Time;
        output.WriteLine($"{states.Count} estados em {seconds:F2} s = {rate:F1} Hz; {socket.BytesReceived} bytes = {kbitPerSecond:F1} kbit/s; tempo simulado {simulated:F2} s");
        output.WriteLine($"exemplo: {socket.LastText}");

        Assert.All(states.Zip(states.Skip(1)), pair => Assert.True(pair.Second.Step > pair.First.Step));
        Assert.All(states.Skip(1), s => Assert.Equal(0, s.Step % 5)); // o primeiro é o da assinatura, de qualquer passo
        Assert.InRange(rate, 15, 25);
        Assert.True(kbitPerSecond <= 100, $"banda de {kbitPerSecond:F1} kbit/s");
    }

    [Fact]
    public async Task OtherProtocolVersion_GetsIncompatible_AndTheServerCloses()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory);
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);

        await socket.SendAsync(new HelloMessage(PlantSocketProtocol.Version + 98));

        Assert.Equal(new IncompatibleMessage(PlantSocketProtocol.Version, PlantSocketProtocol.Version + 98), await socket.ReceiveAsync());
        Assert.Null(await socket.ReceiveAsync());
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.CloseStatus);
    }

    [Fact]
    public async Task FirstMessageNotHello_IsAFatalError()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory);
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);

        await socket.SendAsync(new RestartCommand());

        Assert.Equal(new SocketErrorMessage(SocketErrorCodes.HelloExpected, Fatal: true), await socket.ReceiveAsync());
        Assert.Null(await socket.ReceiveAsync());
    }

    [Fact]
    public async Task UnknownPlant_IsAFatalError()
    {
        using var factory = new SimulationFactory();
        await using var socket = await PlantSocketClient.ConnectAsync(factory, Guid.NewGuid());

        await socket.SendAsync(new HelloMessage(PlantSocketProtocol.Version));

        Assert.Equal(new SocketErrorMessage(SocketErrorCodes.PlantNotFound, Fatal: true), await socket.ReceiveAsync());
        Assert.Null(await socket.ReceiveAsync());
    }

    /// <summary>
    /// O distúrbio pelo WebSocket abre FV-103: a vazão de vapor sobe e a pressão cai; o valor entra na gravação.
    /// </summary>
    [Fact]
    public async Task Disturbance_MovesSteamFlowAndPressure_AndIsRecorded()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory, record: true);
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        WelcomeMessage welcome = await socket.HelloAsync();
        int steamFlow = Index(welcome.Outputs, "FT-102");
        int pressure = Index(welcome.Outputs, "PT-101");
        int command = Index(welcome.Inputs, "FV-103_command");

        StateMessage before = await socket.NextStateAsync();
        await socket.SendAsync(new SetDisturbanceCommand("FV-103_command", 0.9));
        StateMessage after = await socket.NextStateAsync(s => s.Time > before.Time + 8.0);

        output.WriteLine($"FT-102: {before.Outputs[steamFlow]:F3} → {after.Outputs[steamFlow]:F3} kg/s; PT-101: {before.Outputs[pressure] / 1e5:F3} → {after.Outputs[pressure] / 1e5:F3} bar");
        Assert.Equal(0.9f, after.Inputs[command]);
        Assert.True(after.Outputs[steamFlow] > before.Outputs[steamFlow] + 0.5f);
        Assert.True(after.Outputs[pressure] < before.Outputs[pressure] - 0.2e5f);

        using HttpClient client = factory.CreateClient();
        string csv = await client.GetStringAsync($"/api/plants/{plant.Id}/recording", Cancel);
        string[] columns = csv.Split('\n').First(l => l.StartsWith("step,", StringComparison.Ordinal)).Split(',');
        int column = Array.IndexOf(columns, "FV-103_command");
        Assert.Contains(csv.Split('\n'), line => line.Length > 0 && char.IsDigit(line[0]) && line.Split(',')[column] == "0.9");
    }

    [Fact]
    public async Task UnknownDisturbance_IsANonFatalError()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory);
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();

        await socket.SendAsync(new SetDisturbanceCommand("FV-101_command", 0.1));

        Assert.Equal(new SocketErrorMessage(SocketErrorCodes.DisturbanceNotFound, Fatal: false), await socket.NextAsync<SocketErrorMessage>());
        await socket.NextStateAsync(); // a conexão continua
    }

    [Fact]
    public async Task Restart_SendsTheInitialStateWithTheNextRunNumber()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory);
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();
        StateMessage running = await socket.NextStateAsync(s => s.Time > 1.0);

        await socket.SendAsync(new RestartCommand());

        StateMessage restarted = await socket.NextStateAsync(s => s.Run == 1);
        output.WriteLine($"antes: passo {running.Step}, reinício {running.Run}; depois: passo {restarted.Step}, reinício {restarted.Run}");
        Assert.True(restarted.Step < running.Step);
        Assert.Equal(0, running.Run);
    }

    /// <summary>Um mestre conecta, o pedido chega pelo WebSocket, e o aceite pelo WebSocket o autoriza.</summary>
    [Fact]
    public async Task PairingRequest_ArrivesAndIsAcceptedThroughTheSocket()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory);
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();
        ModbusStatusMessage initial = await socket.NextAsync<ModbusStatusMessage>();
        Assert.Empty(initial.PendingRequests);
        Assert.Equal(plant.ModbusPort, initial.Port);

        using RawModbusClient master = await RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, plant.ModbusPort));
        ModbusStatusMessage pending = await socket.NextAsync<ModbusStatusMessage>(m => m.PendingRequests.Count == 1);
        PairingRequestResponse request = Assert.Single(pending.PendingRequests);
        Assert.Equal("127.0.0.1", request.Address);

        await socket.SendAsync(new AcceptPairingCommand(request.Id));

        ModbusStatusMessage accepted = await socket.NextAsync<ModbusStatusMessage>(m => m.AuthorizedMasters.Count == 1);
        Assert.Equal(["127.0.0.1"], accepted.AuthorizedMasters);
        Assert.Empty(accepted.PendingRequests);
        Assert.True(Assert.Single(accepted.Connections).Authorized);
        byte[] reply = await master.RequestAsync(1, 0x03, 0x00, 0x00, 0x00, 0x01); // lê o holding register 0
        Assert.Equal([0x03, 0x02], reply[..2]);
    }

    [Fact]
    public async Task RefusingThroughTheSocket_ClosesTheMaster()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory);
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();

        using RawModbusClient master = await RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, plant.ModbusPort));
        ModbusStatusMessage pending = await socket.NextAsync<ModbusStatusMessage>(m => m.PendingRequests.Count == 1);
        await socket.SendAsync(new RefusePairingCommand(pending.PendingRequests[0].Id));

        Assert.True(await master.WasClosedByServerAsync());
        await socket.NextAsync<ModbusStatusMessage>(m => m.PendingRequests.Count == 0 && m.Connections.Count == 0);

        await socket.SendAsync(new AcceptPairingCommand(pending.PendingRequests[0].Id));
        Assert.Equal(SocketErrorCodes.PairingRequestNotFound, (await socket.NextAsync<SocketErrorMessage>()).Code);
    }

    /// <summary>A conexão conta como cliente; ao encerrar a planta, a IHM recebe o erro fatal e a conexão fecha.</summary>
    [Fact]
    public async Task TheSocketIsAClient_AndEndingThePlantClosesIt()
    {
        using var factory = new SimulationFactory();
        PlantSummary plant = await CreatePlant(factory);
        var manager = factory.Services.GetRequiredService<InstanceManager>();
        Assert.True(manager.TryGet(plant.Id, out PlantInstance? instance));
        await using var socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();
        Assert.Equal(1, instance.ClientCount);

        using HttpClient client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/plants/{plant.Id}", Cancel)).StatusCode);

        Assert.Equal(new SocketErrorMessage(SocketErrorCodes.PlantEnded, Fatal: true), await socket.NextAsync<SocketErrorMessage>());
        Assert.Null(await socket.ReceiveAsync());
        var watch = Stopwatch.StartNew();
        while (instance.ClientCount > 0 && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20, Cancel);
        }

        Assert.Equal(0, instance.ClientCount);
    }

    private static async Task<PlantSummary> CreatePlant(SimulationFactory factory, bool record = false)
    {
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira", Record: record), Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<PlantSummary>(Cancel))!;
    }

    private static int Index(IReadOnlyList<SignalInfo> signals, string name) => signals.ToList().FindIndex(s => s.Name == name);

    /// <summary>Cliente do WebSocket da planta sobre o servidor de teste.</summary>
    private sealed class PlantSocketClient : IAsyncDisposable
    {
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
            var buffer = new byte[64 * 1024];
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
}
