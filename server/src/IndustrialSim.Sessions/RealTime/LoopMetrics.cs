namespace IndustrialSim.Sessions.RealTime;

/// <summary>Métricas do laço inteiro, somadas sobre todas as threads, desde o início.</summary>
/// <param name="ThreadCount">Threads do laço.</param>
/// <param name="PlantCount">Plantas sendo avançadas.</param>
/// <param name="WaiterKind">Mecanismo de espera em uso.</param>
/// <param name="Cycles">Ciclos executados (cada ciclo avança um passo cada planta da thread).</param>
/// <param name="Steps">Passos de planta executados.</param>
/// <param name="LateCycles">Ciclos executados com atraso de um passo inteiro ou mais (recuperação).</param>
/// <param name="DroppedCycles">Ciclos descartados por atraso maior que a recuperação permitida.</param>
/// <param name="Overruns">Vezes em que o atraso passou da recuperação permitida.</param>
/// <param name="StepLateness">Atraso do início de cada passo de planta em relação ao horário previsto.</param>
/// <param name="StepCost">Tempo de relógio gasto em cada passo de planta.</param>
public sealed record LoopMetrics(
    int ThreadCount,
    int PlantCount,
    string WaiterKind,
    long Cycles,
    long Steps,
    long LateCycles,
    long DroppedCycles,
    long Overruns,
    DurationSummary StepLateness,
    DurationSummary StepCost);

/// <summary>Quanto uma planta acompanhou o relógio desde o primeiro passo.</summary>
/// <param name="Steps">Passos executados.</param>
/// <param name="SimulatedSeconds">Passos × dt.</param>
/// <param name="WallSeconds">Tempo de relógio desde o horário previsto do primeiro passo.</param>
/// <param name="Drift">(relógio − simulado) / relógio; positivo quando a simulação está atrás do relógio.</param>
public sealed record RealTimeTracking(long Steps, double SimulatedSeconds, double WallSeconds, double Drift);
