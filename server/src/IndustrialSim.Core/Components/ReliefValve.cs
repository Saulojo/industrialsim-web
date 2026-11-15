namespace IndustrialSim.Core.Components;

/// <summary>
/// Válvula de alívio e segurança de ação rápida ("pop"): abre de uma vez na pressão de ajuste e só fecha
/// quando a pressão cai até a pressão de fechamento (o "blowdown" é a diferença entre as duas).
/// Aberta, descarrega uma vazão proporcional à pressão manométrica: ṁ = ṁ_nominal·(P − P_contra)/(P_ajuste − P_contra),
/// isto é, a vazão nominal é a descarregada na pressão de ajuste.
/// Fontes: API Standard 520, Part I, "Sizing, Selection, and Installation of Pressure-relieving Devices" (termos:
/// set pressure, blowdown, reseating pressure; no dimensionamento para vapor, a vazão por bocal crítico é
/// proporcional à pressão a montante); ASME BPVC, Section I, PG-67 a PG-73 (válvulas de segurança de caldeiras).
/// </summary>
/// <remarks>
/// Simplificação: a proporcionalidade usa a pressão manométrica em vez da absoluta, para a vazão ir a zero junto
/// com a do modelo de pressão v1 (<see cref="SteamPressure"/>). É proteção mecânica: não recebe comando.
/// </remarks>
public sealed class ReliefValve : Component
{
    public const string PressureInput = "pressure";
    public const string OpenOutput = "open";
    public const string FlowOutput = "flow";
    public const string OpenState = "open";

    private readonly int _pressureIndex;
    private readonly int _openStateIndex;
    private readonly int _openOutputIndex;
    private readonly int _flowIndex;

    /// <param name="name">Nome da instância.</param>
    /// <param name="setPressurePa">Pressão de ajuste (abertura), em Pa absolutos.</param>
    /// <param name="reseatPressurePa">Pressão de fechamento, em Pa absolutos (entre a contrapressão e a de ajuste).</param>
    /// <param name="ratedFlowKgPerS">Vazão descarregada na pressão de ajuste, em kg/s (≥ 0).</param>
    /// <param name="backPressurePa">Contrapressão na descarga, em Pa absolutos (padrão: 1 atm).</param>
    public ReliefValve(
        string name,
        double setPressurePa,
        double reseatPressurePa,
        double ratedFlowKgPerS,
        double backPressurePa = PhysicalConstants.StandardAtmospherePa)
        : base(name)
    {
        if (!double.IsFinite(ratedFlowKgPerS) || ratedFlowKgPerS < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratedFlowKgPerS), ratedFlowKgPerS, "A vazão nominal deve ser finita e não negativa.");
        }

        if (!(double.IsFinite(setPressurePa) && double.IsFinite(reseatPressurePa) && double.IsFinite(backPressurePa))
            || !(backPressurePa < reseatPressurePa && reseatPressurePa < setPressurePa))
        {
            throw new ArgumentException(
                $"As pressões devem obedecer contrapressão ({backPressurePa} Pa) < fechamento ({reseatPressurePa} Pa) < ajuste ({setPressurePa} Pa).",
                nameof(reseatPressurePa));
        }

        SetPressurePa = setPressurePa;
        ReseatPressurePa = reseatPressurePa;
        RatedFlowKgPerS = ratedFlowKgPerS;
        BackPressurePa = backPressurePa;

        _pressureIndex = Inputs.Add(PressureInput, backPressurePa);
        _openStateIndex = StateSignals.Add(OpenState);
        _openOutputIndex = OutputSignals.Add(OpenOutput);
        _flowIndex = OutputSignals.Add(FlowOutput);
    }

    public double SetPressurePa { get; }

    public double ReseatPressurePa { get; }

    public double RatedFlowKgPerS { get; }

    public double BackPressurePa { get; }

    public double PressurePa
    {
        get => Inputs[_pressureIndex];
        set => Inputs[_pressureIndex] = value;
    }

    public bool IsOpen => Outputs[_openOutputIndex] >= 0.5;

    public double FlowKgPerS => Outputs[_flowIndex];

    protected override void OnInitialize() => Evaluate();

    protected override void Advance(double timeSeconds, double stepSeconds) => Evaluate();

    protected override void ComputeOutputs()
    {
        bool open = StateSignals[_openStateIndex] >= 0.5;
        double gauge = Math.Max(0.0, Inputs[_pressureIndex] - BackPressurePa);
        OutputSignals[_openOutputIndex] = open ? 1.0 : 0.0;
        OutputSignals[_flowIndex] = open ? RatedFlowKgPerS * gauge / (SetPressurePa - BackPressurePa) : 0.0;
    }

    private void Evaluate()
    {
        double pressure = Inputs[_pressureIndex];
        bool open = StateSignals[_openStateIndex] >= 0.5;
        open = open ? pressure > ReseatPressurePa : pressure >= SetPressurePa;
        StateSignals[_openStateIndex] = open ? 1.0 : 0.0;
    }
}
