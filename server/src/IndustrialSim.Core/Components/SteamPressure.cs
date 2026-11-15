using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Components;

/// <summary>
/// Modelo de pressão v1 do vapor: um acumulador de 1ª ordem.
/// C·dP/dt = ṁ_gerado − ṁ_saída − ṁ_alívio, com ṁ_saída = k·x·(P − P_jusante),
/// em que C é a capacitância de vapor do gerador (kg/Pa), k a condutância da linha de saída com a válvula toda
/// aberta (kg/(s·Pa)) e x a fração de abertura da válvula de saída. Com x constante, a pressão responde ao
/// combustível e à abertura como um sistema de 1ª ordem de constante de tempo τ = C/(k·x) e valor de regime
/// P = P_jusante + ṁ_gerado/(k·x).
/// Fontes: versão a coeficientes constantes do balanço global de energia de Åström, K. J.; Bell, R. D.
/// "Drum-boiler dynamics", Automatica, v. 36, n. 3, 2000 (modelo de 2ª ordem: a pressão integra a diferença entre o calor
/// fornecido e o vapor retirado); capacitância e resistência de sistemas de gás em Seborg et al.,
/// "Process Dynamics and Control", 4ª ed., cap. 2.
/// </summary>
/// <remarks>
/// Simplificações: C e k são constantes (no modelo v2, Etapa 15, saem das propriedades da água pela IAPWS-IF97);
/// a vazão de saída é linear na pressão manométrica; a pressão não fica abaixo da de jusante (não há vácuo).
/// </remarks>
public sealed class SteamPressure : Component
{
    public const string SteamGenerationInput = "steamGeneration";
    public const string OutletOpeningInput = "outletOpening";
    public const string ReliefFlowInput = "reliefFlow";
    public const string PressureOutput = "pressure";
    public const string SteamFlowOutput = "steamFlow";
    public const string PressureState = "pressure";

    private readonly Func<double, double, double> _derivative;
    private readonly int _generationIndex;
    private readonly int _openingIndex;
    private readonly int _reliefIndex;
    private readonly int _pressureStateIndex;
    private readonly int _pressureOutputIndex;
    private readonly int _steamFlowIndex;
    private double _heldNetSupplyKgPerS;
    private double _heldOpening;

    /// <param name="name">Nome da instância.</param>
    /// <param name="capacitanceKgPerPa">Capacitância de vapor C, em kg/Pa (&gt; 0).</param>
    /// <param name="outletConductanceKgPerSPa">Condutância k da saída toda aberta, em kg/(s·Pa) (≥ 0).</param>
    /// <param name="downstreamPressurePa">Pressão a jusante da válvula de saída, em Pa absolutos (padrão: 1 atm).</param>
    /// <param name="initialPressurePa">Pressão inicial, em Pa absolutos (limitada à de jusante).</param>
    public SteamPressure(
        string name,
        double capacitanceKgPerPa,
        double outletConductanceKgPerSPa,
        double downstreamPressurePa = PhysicalConstants.StandardAtmospherePa,
        double? initialPressurePa = null)
        : base(name)
    {
        if (!double.IsFinite(capacitanceKgPerPa) || capacitanceKgPerPa <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacitanceKgPerPa), capacitanceKgPerPa, "A capacitância deve ser finita e maior que zero.");
        }

        if (!double.IsFinite(outletConductanceKgPerSPa) || outletConductanceKgPerSPa < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(outletConductanceKgPerSPa), outletConductanceKgPerSPa, "A condutância deve ser finita e não negativa.");
        }

        if (!double.IsFinite(downstreamPressurePa) || downstreamPressurePa < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(downstreamPressurePa), downstreamPressurePa, "A pressão de jusante deve ser finita e não negativa.");
        }

        CapacitanceKgPerPa = capacitanceKgPerPa;
        OutletConductanceKgPerSPa = outletConductanceKgPerSPa;
        DownstreamPressurePa = downstreamPressurePa;

        _generationIndex = Inputs.Add(SteamGenerationInput);
        _openingIndex = Inputs.Add(OutletOpeningInput);
        _reliefIndex = Inputs.Add(ReliefFlowInput);
        _pressureStateIndex = StateSignals.Add(PressureState, Math.Max(downstreamPressurePa, initialPressurePa ?? downstreamPressurePa));
        _pressureOutputIndex = OutputSignals.Add(PressureOutput);
        _steamFlowIndex = OutputSignals.Add(SteamFlowOutput);
        _derivative = Derivative;

        ComputeOutputs();
    }

    public double CapacitanceKgPerPa { get; }

    public double OutletConductanceKgPerSPa { get; }

    public double DownstreamPressurePa { get; }

    /// <summary>Vapor gerado, em kg/s.</summary>
    public double SteamGenerationKgPerS
    {
        get => Inputs[_generationIndex];
        set => Inputs[_generationIndex] = value;
    }

    /// <summary>Fração de abertura da válvula de saída de vapor, de 0 a 1; limitada a essa faixa.</summary>
    public double OutletOpening
    {
        get => Inputs[_openingIndex];
        set => Inputs[_openingIndex] = value;
    }

    /// <summary>Vazão descarregada pela válvula de alívio, em kg/s.</summary>
    public double ReliefFlowKgPerS
    {
        get => Inputs[_reliefIndex];
        set => Inputs[_reliefIndex] = value;
    }

    /// <summary>Pressão do vapor, em Pa absolutos.</summary>
    public double PressurePa => Outputs[_pressureOutputIndex];

    /// <summary>Vazão instantânea de vapor pela válvula de saída, em kg/s.</summary>
    public double SteamFlowKgPerS => Outputs[_steamFlowIndex];

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        _heldOpening = Math.Clamp(Inputs[_openingIndex], 0.0, 1.0);
        _heldNetSupplyKgPerS = Math.Max(0.0, Inputs[_generationIndex]) - Math.Max(0.0, Inputs[_reliefIndex]);

        // A dinâmica mais rápida é a da saída toda aberta, τ = C/(k·x): o passo tem de respeitá-la.
        double conductance = OutletConductanceKgPerSPa * _heldOpening;
        if (conductance > 0.0)
        {
            FirstOrderActuator.EnsureStable(Name, CapacitanceKgPerPa / conductance, stepSeconds);
        }

        double next = Rk4.Step(_derivative, timeSeconds, StateSignals[_pressureStateIndex], stepSeconds);
        StateSignals[_pressureStateIndex] = Math.Max(DownstreamPressurePa, next);
    }

    protected override void ComputeOutputs()
    {
        double pressure = StateSignals[_pressureStateIndex];
        OutputSignals[_pressureOutputIndex] = pressure;
        OutputSignals[_steamFlowIndex] = OutletFlow(pressure, Math.Clamp(Inputs[_openingIndex], 0.0, 1.0));
    }

    private double Derivative(double timeSeconds, double pressure) =>
        (_heldNetSupplyKgPerS - OutletFlow(pressure, _heldOpening)) / CapacitanceKgPerPa;

    private double OutletFlow(double pressure, double opening) =>
        OutletConductanceKgPerSPa * opening * Math.Max(0.0, pressure - DownstreamPressurePa);
}
