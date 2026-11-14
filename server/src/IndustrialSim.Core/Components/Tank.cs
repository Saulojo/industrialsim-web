using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Components;

/// <summary>
/// Tanque atmosférico de seção constante, com entrada por cima e saída por um orifício no fundo.
/// Balanço de massa a densidade constante: dV/dt = Qin − Qout, com h = V/A.
/// Saída pela lei de Torricelli com coeficiente de descarga: Qout = Cd·a·x·√(2·g·h), x = abertura da saída (0 a 1).
/// Fontes: Seborg, Edgar, Mellichamp, Doyle. "Process Dynamics and Control", 4ª ed., Wiley, 2016, seção 2.4
/// (modelos de nível de líquido); White, F. M. "Fluid Mechanics", 8ª ed., seção 3.5 (Torricelli e coeficiente de descarga).
/// </summary>
/// <remarks>
/// O nível fica sempre em [0, altura]. O que passaria da borda sai pela saída <see cref="OverflowOutput"/>,
/// de modo que o balanço ΔV = ∫(Qin − Qout − Qtransbordo)dt continua fechando com o tanque cheio.
/// </remarks>
public sealed class Tank : Component
{
    public const string InflowInput = "inflow";
    public const string OutletOpeningInput = "outletOpening";
    public const string LevelOutput = "level";
    public const string OutflowOutput = "outflow";
    public const string OverflowOutput = "overflow";
    public const string VolumeState = "volume";

    private readonly Func<double, double, double> _derivative;
    private readonly int _inflowIndex;
    private readonly int _openingIndex;
    private readonly int _volumeIndex;
    private readonly int _levelIndex;
    private readonly int _outflowIndex;
    private readonly int _overflowIndex;
    private readonly double _outletGain; // Cd·a·√(2g), em m^2,5/s
    private double _heldInflowM3PerS;
    private double _heldOpening;
    private double _overflowM3PerS;

    /// <param name="name">Nome da instância.</param>
    /// <param name="areaM2">Área da seção transversal, em m² (&gt; 0).</param>
    /// <param name="heightM">Altura do tanque, em m (&gt; 0).</param>
    /// <param name="outletAreaM2">Área do orifício de saída totalmente aberto, em m² (≥ 0).</param>
    /// <param name="dischargeCoefficient">Coeficiente de descarga Cd (0 &lt; Cd ≤ 1); 0,61 é o valor típico de orifício de borda viva.</param>
    /// <param name="initialLevelM">Nível inicial, em m (limitado a [0, altura]).</param>
    public Tank(
        string name,
        double areaM2,
        double heightM,
        double outletAreaM2,
        double dischargeCoefficient = 0.61,
        double initialLevelM = 0.0)
        : base(name)
    {
        if (!double.IsFinite(areaM2) || areaM2 <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(areaM2), areaM2, "A área deve ser finita e maior que zero.");
        }

        if (!double.IsFinite(heightM) || heightM <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightM), heightM, "A altura deve ser finita e maior que zero.");
        }

        if (!double.IsFinite(outletAreaM2) || outletAreaM2 < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(outletAreaM2), outletAreaM2, "A área da saída deve ser finita e não negativa.");
        }

        if (!(dischargeCoefficient > 0.0 && dischargeCoefficient <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(dischargeCoefficient), dischargeCoefficient, "O coeficiente de descarga deve estar em (0, 1].");
        }

        AreaM2 = areaM2;
        HeightM = heightM;
        OutletAreaM2 = outletAreaM2;
        DischargeCoefficient = dischargeCoefficient;
        _outletGain = dischargeCoefficient * outletAreaM2 * Math.Sqrt(2.0 * PhysicalConstants.StandardGravityMPerS2);

        _inflowIndex = Inputs.Add(InflowInput);
        _openingIndex = Inputs.Add(OutletOpeningInput);
        _volumeIndex = StateSignals.Add(VolumeState, Math.Clamp(initialLevelM, 0.0, heightM) * areaM2);
        _levelIndex = OutputSignals.Add(LevelOutput);
        _outflowIndex = OutputSignals.Add(OutflowOutput);
        _overflowIndex = OutputSignals.Add(OverflowOutput);
        _derivative = VolumeDerivative;

        ComputeOutputs();
    }

    public double AreaM2 { get; }

    public double HeightM { get; }

    public double OutletAreaM2 { get; }

    public double DischargeCoefficient { get; }

    public double CapacityM3 => AreaM2 * HeightM;

    /// <summary>Vazão de entrada, em m³/s. Valores negativos são tratados como zero.</summary>
    public double InflowM3PerS
    {
        get => Inputs[_inflowIndex];
        set => Inputs[_inflowIndex] = value;
    }

    /// <summary>Abertura da saída, de 0 (fechada) a 1 (aberta); limitada a essa faixa.</summary>
    public double OutletOpening
    {
        get => Inputs[_openingIndex];
        set => Inputs[_openingIndex] = value;
    }

    public double VolumeM3 => States[_volumeIndex];

    public double LevelM => Outputs[_levelIndex];

    /// <summary>Vazão de saída instantânea, em m³/s, para o nível e a abertura atuais.</summary>
    public double OutflowM3PerS => Outputs[_outflowIndex];

    /// <summary>Vazão média de transbordo no último passo, em m³/s.</summary>
    public double OverflowM3PerS => Outputs[_overflowIndex];

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        _heldInflowM3PerS = Math.Max(0.0, Inputs[_inflowIndex]);
        _heldOpening = Math.Clamp(Inputs[_openingIndex], 0.0, 1.0);

        double volume = Rk4.Step(_derivative, timeSeconds, StateSignals[_volumeIndex], stepSeconds);

        double overflowVolume = 0.0;
        if (volume > CapacityM3)
        {
            overflowVolume = volume - CapacityM3;
            volume = CapacityM3;
        }
        else if (volume < 0.0)
        {
            volume = 0.0;
        }

        _overflowM3PerS = overflowVolume / stepSeconds;
        StateSignals[_volumeIndex] = volume;
    }

    protected override void ComputeOutputs()
    {
        double volume = StateSignals[_volumeIndex];
        OutputSignals[_levelIndex] = volume / AreaM2;
        OutputSignals[_outflowIndex] = Outflow(volume, Math.Clamp(Inputs[_openingIndex], 0.0, 1.0));
        OutputSignals[_overflowIndex] = _overflowM3PerS;
    }

    private double VolumeDerivative(double timeSeconds, double volume) => _heldInflowM3PerS - Outflow(volume, _heldOpening);

    /// <summary>Torricelli, com a coluna limitada a [0, altura]: um estágio do RK4 não pode ver nível negativo nem acima da borda.</summary>
    private double Outflow(double volume, double opening)
    {
        double level = Math.Clamp(volume / AreaM2, 0.0, HeightM);
        return _outletGain * opening * Math.Sqrt(level);
    }
}
