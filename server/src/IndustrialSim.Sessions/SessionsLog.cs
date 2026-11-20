using System.Net;
using Microsoft.Extensions.Logging;

namespace IndustrialSim.Sessions;

/// <summary>
/// Mensagens de log estruturadas das plantas em execução (geradas pelo <c>LoggerMessage</c>). São mensagens para
/// quem opera o servidor, não texto de interface; os campos entre chaves viram propriedades no log em JSON.
/// </summary>
internal static partial class SessionsLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Laço de tempo real iniciado: {ThreadCount} threads, passo de {StepMilliseconds} ms, recuperação de até {MaxCatchUpSteps} passos, espera por {WaiterKind}.")]
    public static partial void LoopStarted(ILogger logger, int threadCount, double stepMilliseconds, int maxCatchUpSteps, string waiterKind);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Laço de tempo real encerrado; threads ainda vivas: {AliveThreads}.")]
    public static partial void LoopStopped(ILogger logger, int aliveThreads);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error,
        Message = "A thread {ThreadName} do laço não terminou em {TimeoutSeconds} s.")]
    public static partial void ThreadDidNotStop(ILogger logger, string threadName, double timeoutSeconds);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Thread {ThreadIndex} do laço atrasou mais que a recuperação: {DroppedCycles} ciclos descartados desde o último aviso ({TotalDroppedCycles} no total).")]
    public static partial void CyclesDropped(ILogger logger, int threadIndex, long droppedCycles, long totalDroppedCycles);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error,
        Message = "{StepperName} lançou exceção no passo e saiu da thread {ThreadIndex} do laço.")]
    public static partial void StepperFaulted(ILogger logger, Exception exception, string stepperName, int threadIndex);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error,
        Message = "Erro ao avisar {StepperName} da própria falha.")]
    public static partial void FaultHandlerFailed(ILogger logger, Exception exception, string stepperName);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Critical,
        Message = "Erro no laço de tempo real, thread {ThreadIndex}.")]
    public static partial void LoopIterationFailed(ILogger logger, Exception exception, int threadIndex);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information,
        Message = "Cenário {ScenarioId} carregado de {Path}.")]
    public static partial void ScenarioLoaded(ILogger logger, string scenarioId, string path);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "Cenário ignorado ({Path}): {Reason}")]
    public static partial void ScenarioSkipped(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
        Message = "Pasta de cenários não encontrada: {Path}.")]
    public static partial void ScenarioDirectoryMissing(ILogger logger, string path);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information,
        Message = "Planta {InstanceId} criada: cenário {ScenarioId}, semente {NoiseSeed}, thread {ThreadIndex} do laço ({InstanceCount} plantas em execução).")]
    public static partial void InstanceCreated(ILogger logger, Guid instanceId, string scenarioId, ulong noiseSeed, int threadIndex, int instanceCount);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information,
        Message = "Planta {InstanceId} encerrada ({Reason}) com {SimulatedSeconds} s simulados ({InstanceCount} plantas em execução).")]
    public static partial void InstanceEnded(ILogger logger, Guid instanceId, EndReason reason, double simulatedSeconds, int instanceCount);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Warning,
        Message = "Planta do cenário {ScenarioId} recusada: limite de {MaxInstances} plantas em execução atingido.")]
    public static partial void InstanceLimitReached(ILogger logger, string scenarioId, int maxInstances);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Error,
        Message = "Planta {InstanceId} parou por falha: {Message}")]
    public static partial void InstanceFaulted(ILogger logger, Guid instanceId, string message);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Error,
        Message = "Falha ao escrever a gravação {Path}.")]
    public static partial void RecordingFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning,
        Message = "A gravação {Path} não terminou de ser escrita em {TimeoutSeconds} s.")]
    public static partial void RecordingNotFlushed(ILogger logger, string path, double timeoutSeconds);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Warning,
        Message = "Planta do cenário {ScenarioId} recusada: nenhuma porta Modbus livre entre {PortRangeStart} e {PortRangeEnd}.")]
    public static partial void ModbusPortsExhausted(ILogger logger, string scenarioId, int portRangeStart, int portRangeEnd);

    [LoggerMessage(EventId = 1400, Level = LogLevel.Information,
        Message = "Planta {InstanceId}: pedido de pareamento {RequestId} de {Address}.")]
    public static partial void PairingRequested(ILogger logger, Guid instanceId, Guid requestId, IPAddress address);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information,
        Message = "Planta {InstanceId}: pedido de pareamento {RequestId} de {Address} aceito.")]
    public static partial void PairingAccepted(ILogger logger, Guid instanceId, Guid requestId, IPAddress address);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Warning,
        Message = "Planta {InstanceId}: pedido de pareamento {RequestId} de {Address} recusado.")]
    public static partial void PairingRefused(ILogger logger, Guid instanceId, Guid requestId, IPAddress address);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Warning,
        Message = "Planta {InstanceId}: pedido de pareamento {RequestId} de {Address} sem resposta no tempo limite.")]
    public static partial void PairingExpired(ILogger logger, Guid instanceId, Guid requestId, IPAddress address);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Warning,
        Message = "Planta {InstanceId}: conexão de {Address} fechada; o IP foi recusado há pouco.")]
    public static partial void PairingRecentlyRefused(ILogger logger, Guid instanceId, IPAddress address);

    [LoggerMessage(EventId = 1405, Level = LogLevel.Warning,
        Message = "Planta {InstanceId}: conexão de {Address} fechada; já há {MaxPendingRequests} pedidos de pareamento pendentes.")]
    public static partial void PairingPendingLimit(ILogger logger, Guid instanceId, IPAddress address, int maxPendingRequests);

    [LoggerMessage(EventId = 1406, Level = LogLevel.Information,
        Message = "Planta {InstanceId}: mestre {Address} autorizado.")]
    public static partial void MasterAuthorized(ILogger logger, Guid instanceId, IPAddress address);

    [LoggerMessage(EventId = 1407, Level = LogLevel.Information,
        Message = "Planta {InstanceId}: autorização do mestre {Address} retirada; {ClosedConnections} conexões fechadas.")]
    public static partial void MasterRevoked(ILogger logger, Guid instanceId, IPAddress address, int closedConnections);

    [LoggerMessage(EventId = 1500, Level = LogLevel.Information,
        Message = "Planta {InstanceId}: reiniciada no estado inicial do cenário (reinício {Run}), depois de {StepCount} passos.")]
    public static partial void InstanceRestarted(ILogger logger, Guid instanceId, int run, long stepCount);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Information,
        Message = "Planta {InstanceId}: distúrbio {Input} = {Value}.")]
    public static partial void DisturbanceChanged(ILogger logger, Guid instanceId, string input, double value);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Information,
        Message = "Planta {InstanceId}: configuração {Setting} mudou de {OldValue} para {NewValue}.")]
    public static partial void SettingChanged(ILogger logger, Guid instanceId, string setting, string oldValue, string newValue);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Information,
        Message = "Planta {InstanceId}: situação {Status} no passo {StepCount}.")]
    public static partial void RunStateChanged(ILogger logger, Guid instanceId, InstanceStatus status, long stepCount);
}
