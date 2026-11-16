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
}
