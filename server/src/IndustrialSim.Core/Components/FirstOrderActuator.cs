using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Components;

/// <summary>
/// Atuador de 1ª ordem: a posição segue o comando com constante de tempo τ.
/// Equação: τ·dx/dt = u − x, isto é, G(s) = 1/(τs + 1); ao degrau, x chega a 1 − e⁻¹ ≈ 63,2% em t = τ.
/// Fonte: Seborg, Edgar, Mellichamp, Doyle. "Process Dynamics and Control", 4ª ed., Wiley, 2016, seção 5.2
/// (resposta de 1ª ordem) e cap. 9 (dinâmica de válvulas de controle).
/// </summary>
public sealed class FirstOrderActuator : Component
{
    public const string TargetInput = "target";
    public const string PositionOutput = "position";
    public const string PositionState = "position";

    /// <summary>Limite de estabilidade do RK4 em dx/dt = −x/τ é dt/τ ≈ 2,785; a folga evita operar na borda.</summary>
    internal const double MaxStepToTimeConstantRatio = 2.5;

    private readonly Func<double, double, double> _derivative;
    private readonly int _targetIndex;
    private readonly int _positionStateIndex;
    private readonly int _positionOutputIndex;
    private double _heldTarget;

    /// <param name="name">Nome da instância.</param>
    /// <param name="timeConstantSeconds">Constante de tempo τ, em segundos (&gt; 0).</param>
    /// <param name="minimum">Fim de curso inferior (o comando é limitado a ele).</param>
    /// <param name="maximum">Fim de curso superior.</param>
    /// <param name="initialPosition">Posição inicial.</param>
    public FirstOrderActuator(
        string name,
        double timeConstantSeconds,
        double minimum = 0.0,
        double maximum = 1.0,
        double initialPosition = 0.0)
        : base(name)
    {
        if (!double.IsFinite(timeConstantSeconds) || timeConstantSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeConstantSeconds), timeConstantSeconds, "A constante de tempo deve ser finita e maior que zero.");
        }

        if (!(minimum < maximum))
        {
            throw new ArgumentException($"O limite inferior ({minimum}) deve ser menor que o superior ({maximum}).", nameof(minimum));
        }

        TimeConstantSeconds = timeConstantSeconds;
        Minimum = minimum;
        Maximum = maximum;

        double start = Math.Clamp(initialPosition, minimum, maximum);
        _targetIndex = Inputs.Add(TargetInput, start);
        _positionStateIndex = StateSignals.Add(PositionState, start);
        _positionOutputIndex = OutputSignals.Add(PositionOutput, start);
        _derivative = Derivative;
    }

    public double TimeConstantSeconds { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    /// <summary>Comando (posição desejada).</summary>
    public double Target
    {
        get => Inputs[_targetIndex];
        set => Inputs[_targetIndex] = value;
    }

    /// <summary>Posição real.</summary>
    public double Position => Outputs[_positionOutputIndex];

    /// <summary>
    /// Recusa um passo grande demais para τ. Compartilhado com outros componentes que têm dinâmica de 1ª ordem.
    /// </summary>
    internal static void EnsureStable(string name, double timeConstantSeconds, double stepSeconds)
    {
        if (stepSeconds > MaxStepToTimeConstantRatio * timeConstantSeconds)
        {
            throw new InvalidOperationException(
                $"O passo de {stepSeconds} s é grande demais para a constante de tempo de {timeConstantSeconds} s de '{name}' " +
                $"(limite: {MaxStepToTimeConstantRatio}·τ); o RK4 ficaria instável.");
        }
    }

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        EnsureStable(Name, TimeConstantSeconds, stepSeconds);

        _heldTarget = Math.Clamp(Inputs[_targetIndex], Minimum, Maximum);
        double next = Rk4.Step(_derivative, timeSeconds, StateSignals[_positionStateIndex], stepSeconds);
        StateSignals[_positionStateIndex] = Math.Clamp(next, Minimum, Maximum);
    }

    protected override void ComputeOutputs() => OutputSignals[_positionOutputIndex] = StateSignals[_positionStateIndex];

    private double Derivative(double timeSeconds, double position) => (_heldTarget - position) / TimeConstantSeconds;
}
