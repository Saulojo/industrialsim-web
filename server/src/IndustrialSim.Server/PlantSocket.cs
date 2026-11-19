using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using IndustrialSim.Contracts;
using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;
using IndustrialSim.Modbus;
using IndustrialSim.Sessions;
using IndustrialSim.Sessions.Modbus;

namespace IndustrialSim.Server;

/// <summary>Configuração do WebSocket da planta (seção <c>Simulation:Socket</c> do appsettings).</summary>
public sealed record PlantSocketOptions
{
    /// <summary>
    /// Endereço do servidor que a IHM mostra para o mestre Modbus conectar; vazio para usar o host da página.
    /// </summary>
    public string? PublicAddress { get; init; }

    /// <summary>Prazo para o cliente mandar o <see cref="HelloMessage"/> depois de abrir o WebSocket.</summary>
    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Intervalo dos pings do WebSocket, para notar conexões mortas.</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// WebSocket da planta (D05): <c>/api/plants/{id}/ws</c>. Fala o protocolo de <see cref="PlantSocketProtocol"/>.
/// </summary>
internal static class PlantSocketEndpoint
{
    public static void MapPlantSocket(this RouteGroupBuilder plants) => plants.Map("/{id:guid}/ws", HandleAsync);

    private static async Task HandleAsync(
        HttpContext context,
        Guid id,
        InstanceManager manager,
        PlantSocketOptions options,
        ILogger<PlantSocketSession> logger)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync(
            new WebSocketAcceptContext { KeepAliveInterval = options.KeepAliveInterval });
        var session = new PlantSocketSession(socket, id, manager, options, logger);
        await session.RunAsync(context.RequestAborted);
    }
}

/// <summary>
/// Uma conexão da IHM com uma planta: confere a versão, manda o <see cref="WelcomeMessage"/>, repassa o estado da
/// planta a cada período e os eventos quando mudam, e executa os comandos do cliente.
/// </summary>
/// <remarks>
/// Duas tarefas por conexão: a que envia (acordada pelo laço da planta a cada estado publicado) e a que recebe.
/// Os envios passam por uma trava, porque o WebSocket aceita um envio por vez. Enquanto a conexão existe, ela
/// conta como cliente da planta, que então não expira.
/// </remarks>
internal sealed partial class PlantSocketSession(
    WebSocket socket,
    Guid plantId,
    InstanceManager manager,
    PlantSocketOptions options,
    ILogger<PlantSocketSession> logger)
{
    /// <summary>Maior mensagem aceita do cliente.</summary>
    public const int MaxClientMessageBytes = 4096;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private long _bytesSent;

    public async Task RunAsync(CancellationToken aborted)
    {
        HelloMessage? hello = await ReceiveHelloAsync(aborted);
        if (hello is null)
        {
            return;
        }

        if (hello.ProtocolVersion != PlantSocketProtocol.Version)
        {
            LogIncompatible(logger, plantId, hello.ProtocolVersion, PlantSocketProtocol.Version);
            await SendAsync(new IncompatibleMessage(PlantSocketProtocol.Version, hello.ProtocolVersion), aborted);
            await CloseAsync(WebSocketCloseStatus.PolicyViolation, "incompatible", aborted);
            await WaitForCloseAsync(aborted);
            return;
        }

        if (!manager.TryGet(plantId, out PlantInstance? plant))
        {
            await SendFatalAsync(SocketErrorCodes.PlantNotFound, aborted);
            return;
        }

        using IDisposable client = plant.AttachClient();
        using PlantSubscription subscription = plant.Subscribe();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        LogOpened(logger, plantId);

        await SendAsync(Welcome(plant), stop.Token);
        Task receiving = ReceiveCommandsAsync(plant, stop);
        try
        {
            await SendStatesAsync(plant, subscription, stop.Token);
            if (!stop.IsCancellationRequested)
            {
                // A assinatura terminou: a planta foi encerrada.
                await SendFatalAsync(SocketErrorCodes.PlantEnded, stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // O cliente fechou ou caiu.
        }
        finally
        {
            await stop.CancelAsync();
            await receiving.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            double seconds = _elapsed.Elapsed.TotalSeconds;
            LogClosed(logger, plantId, _bytesSent, seconds, seconds > 0 ? _bytesSent * 8 / 1000.0 / seconds : 0.0);
        }
    }

    private async Task<HelloMessage?> ReceiveHelloAsync(CancellationToken aborted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        timeout.CancelAfter(options.HelloTimeout);
        ClientMessage? message;
        try
        {
            message = await ReceiveAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            if (!aborted.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await SendFatalAsync(SocketErrorCodes.HelloExpected, aborted);
            }

            return null;
        }
        catch (JsonException)
        {
            await SendFatalAsync(SocketErrorCodes.HelloExpected, aborted);
            return null;
        }

        if (message is HelloMessage hello)
        {
            return hello;
        }

        if (socket.State == WebSocketState.Open)
        {
            await SendFatalAsync(SocketErrorCodes.HelloExpected, aborted);
        }

        return null;
    }

    private async Task SendStatesAsync(PlantInstance plant, PlantSubscription subscription, CancellationToken cancel)
    {
        long alarmsVersion = -1;
        byte[]? modbus = null;
        await foreach (PlantSnapshot snapshot in subscription.Reader.ReadAllAsync(cancel))
        {
            await SendAsync(State(snapshot), cancel);

            if (snapshot.Alarms.Version != alarmsVersion)
            {
                alarmsVersion = snapshot.Alarms.Version;
                await SendAsync(Alarms(snapshot.Alarms), cancel);
            }

            // Pedidos e conexões não passam pelo laço: confere a cada estado e manda só quando muda.
            byte[] status = Serialize(ModbusStatus(plant.Modbus));
            if (modbus is null || !status.AsSpan().SequenceEqual(modbus))
            {
                modbus = status;
                await SendBytesAsync(status, cancel);
            }
        }
    }

    private async Task ReceiveCommandsAsync(PlantInstance plant, CancellationTokenSource stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                ClientMessage? message;
                try
                {
                    message = await ReceiveAsync(stop.Token);
                }
                catch (JsonException)
                {
                    await SendAsync(new SocketErrorMessage(SocketErrorCodes.InvalidMessage, Fatal: false), stop.Token);
                    continue;
                }

                if (message is null)
                {
                    break; // o cliente fechou
                }

                plant.Touch();
                string? error = Execute(plant, message);
                if (error is not null)
                {
                    await SendAsync(new SocketErrorMessage(error, Fatal: false), stop.Token);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            await stop.CancelAsync();
        }
    }

    /// <summary>Executa um comando; devolve o código de erro, ou nulo se deu certo.</summary>
    private static string? Execute(PlantInstance plant, ClientMessage message) => message switch
    {
        SetDisturbanceCommand c when plant.Scenario.Definition.Hmi?.Disturbances.Any(d => d.Input == c.Input) != true =>
            SocketErrorCodes.DisturbanceNotFound,
        SetDisturbanceCommand c => plant.SetDisturbance(c.Input, c.Value) ? null : SocketErrorCodes.PlantNotRunning,
        RestartCommand => plant.Restart() ? null : SocketErrorCodes.PlantNotRunning,
        AcceptPairingCommand c => plant.Modbus.Pairing.Accept(c.RequestId) ? null : SocketErrorCodes.PairingRequestNotFound,
        RefusePairingCommand c => plant.Modbus.Pairing.Refuse(c.RequestId) ? null : SocketErrorCodes.PairingRequestNotFound,
        _ => SocketErrorCodes.InvalidMessage,
    };

    /// <summary>Lê uma mensagem de texto inteira; nulo quando o cliente fecha. Lança <see cref="JsonException"/>.</summary>
    private async Task<ClientMessage?> ReceiveAsync(CancellationToken cancel)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxClientMessageBytes);
        try
        {
            int length = 0;
            while (true)
            {
                if (length == MaxClientMessageBytes)
                {
                    await CloseAsync(WebSocketCloseStatus.MessageTooBig, "too-big", cancel);
                    return null;
                }

                ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(length, MaxClientMessageBytes - length), cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancel);
                    return null;
                }

                length += result.Count;
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return JsonSerializer.Deserialize<ClientMessage>(buffer.AsSpan(0, length), Json)
                ?? throw new JsonException("Mensagem nula.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private Task SendAsync(ServerMessage message, CancellationToken cancel) => SendBytesAsync(Serialize(message), cancel);

    private async Task SendBytesAsync(byte[] bytes, CancellationToken cancel)
    {
        await _sendLock.WaitAsync(cancel);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancel);
            _bytesSent += bytes.Length;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendFatalAsync(string code, CancellationToken cancel)
    {
        try
        {
            await SendAsync(new SocketErrorMessage(code, Fatal: true), cancel);
            await CloseAsync(WebSocketCloseStatus.NormalClosure, code, cancel);
            await WaitForCloseAsync(cancel);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
    }

    /// <summary>
    /// Depois de mandar o fechamento, espera o do cliente (o aperto de mão do WebSocket), descartando o que
    /// chegar antes, por até 2 s. Sem isso, o cliente veria a conexão cair em vez de fechar.
    /// </summary>
    private async Task WaitForCloseAsync(CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxClientMessageBytes);
        try
        {
            while (socket.State == WebSocketState.CloseSent)
            {
                ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken cancel)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _sendLock.WaitAsync(cancel);
            try
            {
                await socket.CloseOutputAsync(status, description, cancel);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }

    private static byte[] Serialize(ServerMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);

    private WelcomeMessage Welcome(PlantInstance plant)
    {
        ScenarioDefinition definition = plant.Scenario.Definition;
        IoMap map = plant.Io.Map;
        IReadOnlyList<DisturbanceDefinition> disturbances = definition.Hmi?.Disturbances ?? [];

        return new WelcomeMessage(
            PlantSocketProtocol.Version,
            ServerVersion.Current,
            SimulationApi.ToSummary(plant),
            definition.StepSeconds,
            plant.PublishPeriodSeconds,
            string.IsNullOrWhiteSpace(options.PublicAddress) ? null : options.PublicAddress,
            [.. definition.Inputs.Select(i => InputInfo(i.Name, map, disturbances))],
            [.. definition.Outputs.Select(o => OutputInfo(o.Name, plant.Io))],
            [.. disturbances.Select(d => new DisturbanceInfo(d.Input, d.Min, d.Max, d.Unit, d.UnitFactor))],
            plant.AlarmSignals);
    }

    private static SignalInfo InputInfo(string name, IoMap map, IReadOnlyList<DisturbanceDefinition> disturbances)
    {
        if (map.Coils.FirstOrDefault(p => p.Signal == name) is { } coil)
        {
            return new SignalInfo(name, SignalKinds.Digital, string.Empty, 1.0, null, null, Point(ModbusArea.Coil, coil.Address));
        }

        if (map.HoldingRegisters.FirstOrDefault(p => p.Signal == name) is { } holding)
        {
            return new SignalInfo(name, SignalKinds.Analog, holding.Unit, holding.UnitFactor, holding.Lrv, holding.Urv, Point(ModbusArea.HoldingRegister, holding.Address));
        }

        if (disturbances.FirstOrDefault(d => d.Input == name) is { } disturbance)
        {
            return new SignalInfo(name, SignalKinds.Analog, disturbance.Unit, disturbance.UnitFactor, disturbance.Min, disturbance.Max, null);
        }

        return new SignalInfo(name, SignalKinds.Analog, string.Empty, 1.0, null, null, null);
    }

    private static SignalInfo OutputInfo(string name, PlantIo io)
    {
        IoMap map = io.Map;
        if (map.DiscreteInputs.FirstOrDefault(p => p.Signal == name) is { } discrete)
        {
            return new SignalInfo(name, SignalKinds.Digital, string.Empty, 1.0, null, null, Point(ModbusArea.DiscreteInput, discrete.Address));
        }

        if (map.InputRegisters.FirstOrDefault(p => p.Signal == name) is { } register)
        {
            (double lrv, double urv) = io.GetRange(register.Address);
            return new SignalInfo(name, SignalKinds.Analog, register.Unit, register.UnitFactor, lrv, urv, Point(ModbusArea.InputRegister, register.Address));
        }

        int diagnostic = map.Diagnostics.ToList().FindIndex(d => d.Signal == name);
        if (diagnostic >= 0 && map.DiagnosticsAddress is int start)
        {
            DiagnosticPoint point = map.Diagnostics[diagnostic];
            return new SignalInfo(name, SignalKinds.Analog, point.Unit, point.UnitFactor, null, null, Point(ModbusArea.InputRegister, start + (2 * diagnostic)));
        }

        return new SignalInfo(name, SignalKinds.Analog, string.Empty, 1.0, null, null, null);
    }

    private static ModbusPointInfo Point(ModbusArea area, int address) => new(
        area switch
        {
            ModbusArea.Coil => ModbusAreas.Coil,
            ModbusArea.DiscreteInput => ModbusAreas.DiscreteInput,
            ModbusArea.HoldingRegister => ModbusAreas.HoldingRegister,
            _ => ModbusAreas.InputRegister,
        },
        address,
        ModbusAddress.Classic(area, address));

    private static StateMessage State(PlantSnapshot snapshot) => new(
        snapshot.StepCount,
        snapshot.SimulatedSeconds,
        snapshot.Run,
        SimulationApi.ToStatus(snapshot.Status),
        snapshot.WatchdogTripped,
        [.. snapshot.Inputs.Select(v => (float)v)],
        [.. snapshot.Outputs.Select(v => (float)v)]);

    private static AlarmsMessage Alarms(AlarmSet alarms) =>
        new([.. alarms.Active.Select(a => new ActiveAlarm(a.Signal, a.SinceSeconds, a.SinceUtc))]);

    private static ModbusStatusMessage ModbusStatus(PlantModbus modbus) => new(
        modbus.Port,
        modbus.UnitId,
        [.. modbus.Pairing.Authorized.Select(a => a.ToString())],
        [.. modbus.Pairing.Pending.Select(p => new PairingRequestResponse(p.Id, p.Address.ToString(), p.RequestedUtc, p.ExpiresUtc, p.WaitingConnections))],
        [.. modbus.Connections.Select(c => new MasterConnectionInfo(
            c.Id,
            ModbusTcpServer.Normalize(c.Remote.Address).ToString(),
            c.Remote.Port,
            c.ConnectedUtc,
            c.Authorized))]);

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Planta {PlantId}: IHM conectada pelo WebSocket.")]
    private static partial void LogOpened(ILogger logger, Guid plantId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Planta {PlantId}: WebSocket fechado; {BytesSent} bytes enviados em {Seconds:F1} s ({KbitPerSecond:F1} kbit/s).")]
    private static partial void LogClosed(ILogger logger, Guid plantId, long bytesSent, double seconds, double kbitPerSecond);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Planta {PlantId}: cliente com protocolo {ClientVersion} recusado; o servidor fala o {ServerVersion}.")]
    private static partial void LogIncompatible(ILogger logger, Guid plantId, int clientVersion, int serverVersion);
}
