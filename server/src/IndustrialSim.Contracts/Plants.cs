namespace IndustrialSim.Contracts;

/// <summary>Cenário que o servidor pode rodar (<c>GET /api/scenarios</c>). Nomes de tela vêm dos arquivos de idioma.</summary>
/// <param name="Id">Identificador do cenário (ex.: <c>caldeira</c>).</param>
/// <param name="StepSeconds">Passo da simulação, em segundos.</param>
/// <param name="NoiseSeed">Semente padrão do ruído dos instrumentos.</param>
/// <param name="Inputs">Entradas externas da planta.</param>
/// <param name="Outputs">Saídas externas da planta.</param>
public sealed record ScenarioSummary(
    string Id,
    double StepSeconds,
    ulong NoiseSeed,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs);

/// <summary>Corpo de <c>POST /api/plants</c>.</summary>
/// <param name="ScenarioId">Cenário a rodar.</param>
/// <param name="NoiseSeed">Semente do ruído; nula para usar a do cenário.</param>
/// <param name="Record">Gravar o ensaio em CSV desde o primeiro passo.</param>
public sealed record PlantCreateRequest(string ScenarioId, ulong? NoiseSeed = null, bool Record = false);

/// <summary>Situação de uma planta: <c>running</c>, <c>faulted</c> ou <c>ended</c>.</summary>
public static class PlantStatuses
{
    public const string Running = "running";
    public const string Faulted = "faulted";
    public const string Ended = "ended";
}

/// <summary>Uma planta em execução (<c>GET /api/plants</c>).</summary>
/// <param name="Id">Identificador da planta.</param>
/// <param name="ScenarioId">Cenário.</param>
/// <param name="NoiseSeed">Semente do ruído.</param>
/// <param name="Status">Situação (<see cref="PlantStatuses"/>).</param>
/// <param name="CreatedUtc">Criação.</param>
/// <param name="LastActivityUtc">Última atividade; sem clientes, a planta expira um tempo depois dela.</param>
/// <param name="ClientCount">Clientes ligados à planta.</param>
/// <param name="Recording">Se o ensaio está sendo gravado.</param>
public sealed record PlantSummary(
    Guid Id,
    string ScenarioId,
    ulong NoiseSeed,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastActivityUtc,
    int ClientCount,
    bool Recording);

/// <summary>Valor de um sinal externo da planta, em SI.</summary>
public sealed record SignalReading(string Name, double Value);

/// <summary>Quanto a planta acompanhou o relógio (RNF05).</summary>
/// <param name="LoopThread">Thread do laço que avança a planta.</param>
/// <param name="WallSeconds">Tempo de relógio desde o primeiro passo.</param>
/// <param name="SimulatedSeconds">Tempo simulado no mesmo instante.</param>
/// <param name="Drift">(relógio − simulado) / relógio.</param>
public sealed record RealTimeStats(int LoopThread, double WallSeconds, double SimulatedSeconds, double Drift);

/// <summary>Estado de uma planta (<c>GET /api/plants/{id}</c>).</summary>
/// <param name="Plant">Dados da planta.</param>
/// <param name="StepCount">Passos simulados.</param>
/// <param name="SimulatedSeconds">Tempo simulado.</param>
/// <param name="WatchdogTripped">Se o watchdog está disparado.</param>
/// <param name="FaultMessage">Mensagem técnica da falha, se a planta parou por erro.</param>
/// <param name="Inputs">Entradas externas, em SI.</param>
/// <param name="Outputs">Saídas externas, em SI.</param>
/// <param name="RealTime">Acompanhamento do relógio.</param>
public sealed record PlantStateResponse(
    PlantSummary Plant,
    long StepCount,
    double SimulatedSeconds,
    bool WatchdogTripped,
    string? FaultMessage,
    IReadOnlyList<SignalReading> Inputs,
    IReadOnlyList<SignalReading> Outputs,
    RealTimeStats RealTime);

/// <summary>Resumo de uma série de durações, em milissegundos.</summary>
public sealed record DurationStats(long Count, double MeanMs, double P50Ms, double P99Ms, double MaxMs);

/// <summary>Métricas do laço de tempo real (<c>GET /api/simulation/metrics</c>).</summary>
/// <param name="ThreadCount">Threads do laço.</param>
/// <param name="PlantCount">Plantas no laço.</param>
/// <param name="WaiterKind">Mecanismo de espera entre passos.</param>
/// <param name="Cycles">Ciclos executados.</param>
/// <param name="Steps">Passos de planta executados.</param>
/// <param name="LateCycles">Ciclos executados com atraso de um passo ou mais.</param>
/// <param name="DroppedCycles">Ciclos descartados por atraso além da recuperação.</param>
/// <param name="Overruns">Vezes em que o atraso passou da recuperação.</param>
/// <param name="StepLateness">Atraso do início de cada passo de planta.</param>
/// <param name="StepCost">Duração de cada passo de planta.</param>
public sealed record LoopMetricsResponse(
    int ThreadCount,
    int PlantCount,
    string WaiterKind,
    long Cycles,
    long Steps,
    long LateCycles,
    long DroppedCycles,
    long Overruns,
    DurationStats StepLateness,
    DurationStats StepCost);

/// <summary>
/// Erro da API: só um código estável; a interface traduz o código pelos arquivos de idioma (RNF11).
/// </summary>
/// <param name="Code">Código do erro (<see cref="ApiErrorCodes"/>).</param>
public sealed record ApiError(string Code);

/// <summary>Códigos de <see cref="ApiError"/>.</summary>
public static class ApiErrorCodes
{
    public const string ScenarioNotFound = "scenario-not-found";
    public const string PlantNotFound = "plant-not-found";
    public const string InstanceLimitReached = "instance-limit-reached";
    public const string RecordingNotFound = "recording-not-found";
    public const string InvalidRequest = "invalid-request";
}
