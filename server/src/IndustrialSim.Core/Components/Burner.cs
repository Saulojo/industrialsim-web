using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Components;

/// <summary>
/// Queimador e fornalha: transforma vazão de combustível em calor entregue à água.
/// Em regime, Q = η·PCI·ṁ_comb (η = eficiência da caldeira, PCI = poder calorífico inferior). A fornalha e os
/// tubos têm inércia térmica: o calor entregue segue esse valor com dinâmica de 1ª ordem, τ·dQ/dt = η·PCI·ṁ_comb − Q.
/// Fontes: Çengel, Y. A.; Boles, M. A. "Thermodynamics: An Engineering Approach", 8ª ed., McGraw-Hill, 2015,
/// cap. 15 (combustíveis e poder calorífico); a dinâmica de 1ª ordem é a aproximação
/// usual de Seborg et al., "Process Dynamics and Control", 4ª ed., seção 5.2.
/// </summary>
public sealed class Burner : Component
{
    public const string FuelFlowInput = "fuelFlow";
    public const string HeatOutput = "heat";
    public const string HeatState = "heat";

    private readonly Func<double, double, double> _derivative;
    private readonly int _fuelFlowIndex;
    private readonly int _heatStateIndex;
    private readonly int _heatOutputIndex;
    private double _heldTargetW;

    /// <param name="name">Nome da instância.</param>
    /// <param name="lowerHeatingValueJPerKg">Poder calorífico inferior do combustível, em J/kg (&gt; 0).</param>
    /// <param name="efficiency">Fração do calor do combustível que chega à água (0 &lt; η ≤ 1).</param>
    /// <param name="timeConstantSeconds">Constante de tempo da fornalha, em segundos (&gt; 0).</param>
    /// <param name="initialHeatW">Calor entregue no instante inicial, em W (≥ 0).</param>
    public Burner(string name, double lowerHeatingValueJPerKg, double efficiency, double timeConstantSeconds, double initialHeatW = 0.0)
        : base(name)
    {
        if (!double.IsFinite(lowerHeatingValueJPerKg) || lowerHeatingValueJPerKg <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lowerHeatingValueJPerKg), lowerHeatingValueJPerKg, "O poder calorífico deve ser finito e maior que zero.");
        }

        if (!(efficiency > 0.0 && efficiency <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(efficiency), efficiency, "A eficiência deve estar em (0, 1].");
        }

        if (!double.IsFinite(timeConstantSeconds) || timeConstantSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeConstantSeconds), timeConstantSeconds, "A constante de tempo deve ser finita e maior que zero.");
        }

        LowerHeatingValueJPerKg = lowerHeatingValueJPerKg;
        Efficiency = efficiency;
        TimeConstantSeconds = timeConstantSeconds;

        _fuelFlowIndex = Inputs.Add(FuelFlowInput);
        _heatStateIndex = StateSignals.Add(HeatState, Math.Max(0.0, initialHeatW));
        _heatOutputIndex = OutputSignals.Add(HeatOutput);
        _derivative = Derivative;

        ComputeOutputs();
    }

    public double LowerHeatingValueJPerKg { get; }

    public double Efficiency { get; }

    public double TimeConstantSeconds { get; }

    /// <summary>Vazão de combustível, em kg/s. Valores negativos são tratados como zero.</summary>
    public double FuelFlowKgPerS
    {
        get => Inputs[_fuelFlowIndex];
        set => Inputs[_fuelFlowIndex] = value;
    }

    /// <summary>Calor entregue à água, em W.</summary>
    public double HeatW => Outputs[_heatOutputIndex];

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        FirstOrderActuator.EnsureStable(Name, TimeConstantSeconds, stepSeconds);

        _heldTargetW = Efficiency * LowerHeatingValueJPerKg * Math.Max(0.0, Inputs[_fuelFlowIndex]);
        double next = Rk4.Step(_derivative, timeSeconds, StateSignals[_heatStateIndex], stepSeconds);
        StateSignals[_heatStateIndex] = Math.Max(0.0, next);
    }

    protected override void ComputeOutputs() => OutputSignals[_heatOutputIndex] = StateSignals[_heatStateIndex];

    private double Derivative(double timeSeconds, double heatW) => (_heldTargetW - heatW) / TimeConstantSeconds;
}
