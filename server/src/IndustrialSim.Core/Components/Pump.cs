using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Components;

/// <summary>
/// Bomba liga/desliga, comandada só pela entrada <see cref="RunInput"/>: nada dentro dela (nem da planta) a força ligada.
/// A rotação (fração de 0 a 1) segue o comando com dinâmica de 1ª ordem, τ·dn/dt = u − n, e a vazão é
/// proporcional à rotação (lei de afinidade Q ∝ n, com a vazão nominal em n = 1).
/// Fonte: White, F. M. "Fluid Mechanics", 8ª ed., McGraw-Hill, 2016, seção 11.3 (leis de semelhança de bombas).
/// </summary>
/// <remarks>
/// Simplificação desta etapa: a vazão não depende da contrapressão (sem curva de bomba).
/// </remarks>
public sealed class Pump : Component
{
    public const string RunInput = "run";
    public const string FlowOutput = "flow";
    public const string RunningOutput = "running";
    public const string SpeedState = "speed";

    private readonly Func<double, double, double> _derivative;
    private readonly int _runIndex;
    private readonly int _speedIndex;
    private readonly int _flowIndex;
    private readonly int _runningIndex;
    private double _heldTarget;

    /// <param name="name">Nome da instância.</param>
    /// <param name="ratedFlowM3PerS">Vazão nominal, em m³/s (≥ 0).</param>
    /// <param name="timeConstantSeconds">Constante de tempo de partida e parada, em segundos (&gt; 0).</param>
    /// <param name="runningThreshold">Fração da rotação acima da qual a saída "em operação" vale 1.</param>
    public Pump(string name, double ratedFlowM3PerS, double timeConstantSeconds = 1.0, double runningThreshold = 0.5)
        : base(name)
    {
        if (!double.IsFinite(ratedFlowM3PerS) || ratedFlowM3PerS < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratedFlowM3PerS), ratedFlowM3PerS, "A vazão nominal deve ser finita e não negativa.");
        }

        if (!double.IsFinite(timeConstantSeconds) || timeConstantSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeConstantSeconds), timeConstantSeconds, "A constante de tempo deve ser finita e maior que zero.");
        }

        if (!(runningThreshold > 0.0 && runningThreshold < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(runningThreshold), runningThreshold, "O limiar de 'em operação' deve estar entre 0 e 1.");
        }

        RatedFlowM3PerS = ratedFlowM3PerS;
        TimeConstantSeconds = timeConstantSeconds;
        RunningThreshold = runningThreshold;

        _runIndex = Inputs.Add(RunInput);
        _speedIndex = StateSignals.Add(SpeedState);
        _flowIndex = OutputSignals.Add(FlowOutput);
        _runningIndex = OutputSignals.Add(RunningOutput);
        _derivative = Derivative;
    }

    public double RatedFlowM3PerS { get; }

    public double TimeConstantSeconds { get; }

    public double RunningThreshold { get; }

    /// <summary>Comando de ligar. No sinal, vale ligado quando ≥ 0,5.</summary>
    public bool Run
    {
        get => Inputs[_runIndex] >= 0.5;
        set => Inputs[_runIndex] = value ? 1.0 : 0.0;
    }

    public double FlowM3PerS => Outputs[_flowIndex];

    public bool IsRunning => Outputs[_runningIndex] >= 0.5;

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        FirstOrderActuator.EnsureStable(Name, TimeConstantSeconds, stepSeconds);

        _heldTarget = Inputs[_runIndex] >= 0.5 ? 1.0 : 0.0;
        double next = Rk4.Step(_derivative, timeSeconds, StateSignals[_speedIndex], stepSeconds);
        StateSignals[_speedIndex] = Math.Clamp(next, 0.0, 1.0);
    }

    protected override void ComputeOutputs()
    {
        double speed = StateSignals[_speedIndex];
        OutputSignals[_flowIndex] = RatedFlowM3PerS * speed;
        OutputSignals[_runningIndex] = speed >= RunningThreshold ? 1.0 : 0.0;
    }

    private double Derivative(double timeSeconds, double speed) => (_heldTarget - speed) / TimeConstantSeconds;
}
