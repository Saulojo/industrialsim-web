namespace IndustrialSim.Core.Components;

/// <summary>Sentido de atuação de uma chave.</summary>
public enum SwitchDirection
{
    /// <summary>Atua quando o valor sobe até o ponto de ajuste (chaves de alta: LSH, LSHH, PSH).</summary>
    Rising,

    /// <summary>Atua quando o valor cai até o ponto de ajuste (chaves de baixa: LSL, LSLL, PSL).</summary>
    Falling,
}

/// <summary>
/// Chave de processo (nível, pressão…): saída digital que atua no ponto de ajuste e só desatua depois de o valor
/// voltar a banda morta inteira (diferencial), como um pressostato ou uma chave de boia reais.
/// Chave de alta: atua com valor ≥ ajuste; desatua com valor ≤ ajuste − banda morta.
/// Chave de baixa: atua com valor ≤ ajuste; desatua com valor ≥ ajuste + banda morta.
/// Fonte: Lipták, B. G. (ed.). "Instrument Engineers' Handbook, vol. 1: Process Measurement and Analysis",
/// 4ª ed., CRC Press, 2003 (chaves de nível e de pressão: ponto de ajuste e diferencial).
/// </summary>
/// <remarks>O estado é a própria saída (0 ou 1): com banda morta, ela depende do caminho, não só do valor atual.</remarks>
public sealed class ThresholdSwitch : Component
{
    public const string ValueInput = "value";
    public const string ActiveOutput = "active";
    public const string ActiveState = "active";

    private readonly int _valueIndex;
    private readonly int _activeStateIndex;
    private readonly int _activeOutputIndex;

    /// <param name="name">Nome da instância.</param>
    /// <param name="setpoint">Ponto de ajuste, na unidade do valor medido.</param>
    /// <param name="direction">Sentido de atuação.</param>
    /// <param name="deadband">Banda morta (diferencial), na mesma unidade (≥ 0).</param>
    public ThresholdSwitch(string name, double setpoint, SwitchDirection direction, double deadband = 0.0)
        : base(name)
    {
        if (!double.IsFinite(setpoint))
        {
            throw new ArgumentOutOfRangeException(nameof(setpoint), setpoint, "O ponto de ajuste deve ser finito.");
        }

        if (!double.IsFinite(deadband) || deadband < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deadband), deadband, "A banda morta deve ser finita e não negativa.");
        }

        Setpoint = setpoint;
        Direction = direction;
        Deadband = deadband;

        _valueIndex = Inputs.Add(ValueInput);
        _activeStateIndex = StateSignals.Add(ActiveState);
        _activeOutputIndex = OutputSignals.Add(ActiveOutput);
    }

    public double Setpoint { get; }

    public SwitchDirection Direction { get; }

    public double Deadband { get; }

    public double Value
    {
        get => Inputs[_valueIndex];
        set => Inputs[_valueIndex] = value;
    }

    public bool IsActive => Outputs[_activeOutputIndex] >= 0.5;

    protected override void OnInitialize() => Evaluate();

    protected override void Advance(double timeSeconds, double stepSeconds) => Evaluate();

    protected override void ComputeOutputs() => OutputSignals[_activeOutputIndex] = StateSignals[_activeStateIndex];

    private void Evaluate()
    {
        double value = Inputs[_valueIndex];
        bool wasActive = StateSignals[_activeStateIndex] >= 0.5;

        bool active = Direction == SwitchDirection.Rising
            ? value >= Setpoint || (wasActive && value > Setpoint - Deadband)
            : value <= Setpoint || (wasActive && value < Setpoint + Deadband);

        StateSignals[_activeStateIndex] = active ? 1.0 : 0.0;
    }
}
