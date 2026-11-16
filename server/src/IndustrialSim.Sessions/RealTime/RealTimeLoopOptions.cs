using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Sessions.RealTime;

/// <summary>Configuração do laço de tempo real (seção <c>Simulation:Loop</c> do appsettings).</summary>
public sealed record RealTimeLoopOptions
{
    /// <summary>Passo de tempo real, em segundos (D06: 10 ms). Todo cenário rodado no servidor usa este passo.</summary>
    public double StepSeconds { get; init; } = SimulationClock.DefaultStepSeconds;

    /// <summary>Threads do laço; as plantas são distribuídas entre elas.</summary>
    public int ThreadCount { get; init; } = 2;

    /// <summary>
    /// Passos atrasados que uma thread ainda recupera depois de um atraso (padrão: 10, ou seja, 100 ms). Além
    /// disso, os passos são descartados e a planta fica atrás do relógio (aparece na deriva).
    /// </summary>
    public int MaxCatchUpSteps { get; init; } = 10;

    /// <summary>Quanto esperar cada thread terminar ao encerrar o laço.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (!double.IsFinite(StepSeconds) || StepSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(StepSeconds), StepSeconds, "O passo deve ser finito e maior que zero.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ThreadCount, 1, nameof(ThreadCount));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxCatchUpSteps, nameof(MaxCatchUpSteps));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ShutdownTimeout, TimeSpan.Zero, nameof(ShutdownTimeout));
    }
}
