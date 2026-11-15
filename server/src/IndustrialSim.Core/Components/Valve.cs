using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Components;

/// <summary>Característica inerente de vazão de uma válvula de controle.</summary>
public enum ValveCharacteristic
{
    /// <summary>f(x) = x.</summary>
    Linear,

    /// <summary>f(x) = R^(x − 1), com R a rangeabilidade.</summary>
    EqualPercentage,
}

/// <summary>
/// Válvula de controle com atuador: a posição x segue o comando com dinâmica de 1ª ordem (τ·dx/dt = u − x) e a
/// vazão é a vazão disponível na entrada multiplicada pela característica inerente f(x).
/// Linear: f(x) = x. Igual-percentual: f(x) = R^(x − 1), em que R é a rangeabilidade (tipicamente 20 a 50).
/// Fontes: Seborg, Edgar, Mellichamp, Doyle. "Process Dynamics and Control", 4ª ed., Wiley, 2016, seção 9.2
/// (elementos finais de controle: características de válvulas); IEC 60534-2-4:2009 (características inerentes de vazão e rangeabilidade).
/// </summary>
/// <remarks>
/// Simplificações desta versão:
/// - a queda de pressão na válvula é tomada como constante, de modo que a característica instalada é a inerente;
///   a vazão é <c>inletFlow · f(x)</c>, na unidade em que a vazão de entrada vier (m³/s ou kg/s);
/// - a igual-percentual teórica não fecha (f(0) = 1/R). Abaixo de <see cref="ShutoffPosition"/> a curva desce em
///   linha reta até zero, para que comando 0 seja válvula fechada. Essa parte é escolha de modelagem, não da norma.
/// </remarks>
public sealed class Valve : Component
{
    public const string CommandInput = "command";
    public const string InletFlowInput = "inletFlow";
    public const string PositionOutput = "position";
    public const string OpeningOutput = "opening";
    public const string FlowOutput = "flow";
    public const string PositionState = "position";

    /// <summary>Posição abaixo da qual a igual-percentual desce linearmente até zero (2% do curso).</summary>
    public const double ShutoffPosition = 0.02;

    private readonly Func<double, double, double> _derivative;
    private readonly int _commandIndex;
    private readonly int _inletFlowIndex;
    private readonly int _positionStateIndex;
    private readonly int _positionOutputIndex;
    private readonly int _openingIndex;
    private readonly int _flowIndex;
    private double _heldTarget;

    /// <param name="name">Nome da instância.</param>
    /// <param name="timeConstantSeconds">Constante de tempo do atuador, em segundos (&gt; 0).</param>
    /// <param name="characteristic">Característica inerente.</param>
    /// <param name="rangeability">Rangeabilidade R (&gt; 1); só é usada na igual-percentual.</param>
    /// <param name="ratedFlow">Vazão com a válvula toda aberta quando a entrada <see cref="InletFlowInput"/> não é ligada (≥ 0).</param>
    /// <param name="initialPosition">Posição inicial, de 0 a 1.</param>
    public Valve(
        string name,
        double timeConstantSeconds,
        ValveCharacteristic characteristic = ValveCharacteristic.Linear,
        double rangeability = 50.0,
        double ratedFlow = 1.0,
        double initialPosition = 0.0)
        : base(name)
    {
        if (!double.IsFinite(timeConstantSeconds) || timeConstantSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeConstantSeconds), timeConstantSeconds, "A constante de tempo deve ser finita e maior que zero.");
        }

        if (!double.IsFinite(rangeability) || rangeability <= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeability), rangeability, "A rangeabilidade deve ser finita e maior que 1.");
        }

        if (!double.IsFinite(ratedFlow) || ratedFlow < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratedFlow), ratedFlow, "A vazão nominal deve ser finita e não negativa.");
        }

        TimeConstantSeconds = timeConstantSeconds;
        Characteristic = characteristic;
        Rangeability = rangeability;

        double start = Math.Clamp(initialPosition, 0.0, 1.0);
        _commandIndex = Inputs.Add(CommandInput, start);
        _inletFlowIndex = Inputs.Add(InletFlowInput, ratedFlow);
        _positionStateIndex = StateSignals.Add(PositionState, start);
        _positionOutputIndex = OutputSignals.Add(PositionOutput);
        _openingIndex = OutputSignals.Add(OpeningOutput);
        _flowIndex = OutputSignals.Add(FlowOutput);
        _derivative = Derivative;

        ComputeOutputs();
    }

    public double TimeConstantSeconds { get; }

    public ValveCharacteristic Characteristic { get; }

    public double Rangeability { get; }

    /// <summary>Comando (posição desejada), de 0 a 1; limitado a essa faixa.</summary>
    public double Command
    {
        get => Inputs[_commandIndex];
        set => Inputs[_commandIndex] = value;
    }

    /// <summary>Vazão disponível na entrada (a que passaria com a válvula toda aberta).</summary>
    public double InletFlow
    {
        get => Inputs[_inletFlowIndex];
        set => Inputs[_inletFlowIndex] = value;
    }

    /// <summary>Posição real da haste ou do disco, de 0 a 1.</summary>
    public double Position => Outputs[_positionOutputIndex];

    /// <summary>Fração da vazão de entrada que passa, f(x), de 0 a 1.</summary>
    public double Opening => Outputs[_openingIndex];

    public double Flow => Outputs[_flowIndex];

    /// <summary>Característica inerente f(x): fração da vazão máxima na posição <paramref name="position"/> (0 a 1).</summary>
    public static double FlowFraction(ValveCharacteristic characteristic, double position, double rangeability)
    {
        double x = Math.Clamp(position, 0.0, 1.0);
        if (characteristic == ValveCharacteristic.Linear)
        {
            return x;
        }

        return x >= ShutoffPosition
            ? Math.Pow(rangeability, x - 1.0)
            : x / ShutoffPosition * Math.Pow(rangeability, ShutoffPosition - 1.0);
    }

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        FirstOrderActuator.EnsureStable(Name, TimeConstantSeconds, stepSeconds);

        _heldTarget = Math.Clamp(Inputs[_commandIndex], 0.0, 1.0);
        double next = Rk4.Step(_derivative, timeSeconds, StateSignals[_positionStateIndex], stepSeconds);
        StateSignals[_positionStateIndex] = Math.Clamp(next, 0.0, 1.0);
    }

    protected override void ComputeOutputs()
    {
        double position = StateSignals[_positionStateIndex];
        double opening = FlowFraction(Characteristic, position, Rangeability);
        OutputSignals[_positionOutputIndex] = position;
        OutputSignals[_openingIndex] = opening;
        OutputSignals[_flowIndex] = Inputs[_inletFlowIndex] * opening;
    }

    private double Derivative(double timeSeconds, double position) => (_heldTarget - position) / TimeConstantSeconds;
}
