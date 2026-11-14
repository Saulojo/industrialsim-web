namespace IndustrialSim.Core.Simulation;

/// <summary>
/// Relógio de simulação de passo fixo (D06). Não lê o relógio do sistema: o tempo só avança por <see cref="Advance"/>.
/// O tempo é calculado como passos × dt, e não somado a cada passo, para não acumular erro de arredondamento.
/// </summary>
public sealed class SimulationClock
{
    /// <summary>Passo padrão do projeto: 10 ms (RF02).</summary>
    public const double DefaultStepSeconds = 0.01;

    public SimulationClock(double stepSeconds = DefaultStepSeconds)
    {
        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepSeconds), stepSeconds, "O passo deve ser finito e maior que zero.");
        }

        StepSeconds = stepSeconds;
    }

    public double StepSeconds { get; }

    /// <summary>Número de passos já concluídos.</summary>
    public long StepCount { get; private set; }

    /// <summary>Tempo simulado no início do próximo passo.</summary>
    public double TimeSeconds => StepCount * StepSeconds;

    public void Advance() => StepCount++;

    public void Reset() => StepCount = 0;
}
