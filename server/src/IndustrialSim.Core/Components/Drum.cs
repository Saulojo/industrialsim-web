namespace IndustrialSim.Core.Components;

/// <summary>
/// Tubulão v1: balanço de massa da água, sem termodinâmica. dM/dt = ṁ_água − ṁ_evap, com a evaporação dada
/// pelo calor recebido, ṁ_evap = Q/Δh (Δh = entalpia para levar a água de alimentação a vapor saturado), e o
/// nível h = M/(ρ·A), com densidade e seção constantes.
/// Fontes: balanço global de massa de Åström, K. J.; Bell, R. D. "Drum-boiler dynamics", Automatica, v. 36, n. 3,
/// 2000 (modelo de 2ª ordem), aqui com densidade constante e sem a distribuição de vapor abaixo do nível (que é o que gera
/// o swell/shrink e fica para o modelo v2, Etapa 15); nível de líquido em vaso de seção constante em Seborg et al.,
/// "Process Dynamics and Control", 4ª ed., seção 2.4.
/// </summary>
/// <remarks>
/// Simplificações: a seção é constante (um tubulão cilíndrico horizontal tem seção variável com o nível; aqui
/// vale a seção na altura do eixo); a água não muda de densidade com a pressão.
/// A massa fica em [0, capacidade]: sem água não há evaporação (a saída <see cref="SteamGenerationOutput"/> é a
/// evaporação que de fato aconteceu no passo), e o que passaria do topo sai por <see cref="OverflowOutput"/>,
/// de modo que o balanço de massa fecha nos dois limites.
/// Como as entradas valem constantes no passo, a derivada é constante e a integração é exata: M += (ṁ_água − ṁ_evap)·dt.
/// </remarks>
public sealed class Drum : Component
{
    public const string FeedwaterFlowInput = "feedwaterFlow";
    public const string HeatInput = "heat";
    public const string LevelOutput = "level";
    public const string SteamGenerationOutput = "steamGeneration";
    public const string OverflowOutput = "overflow";
    public const string WaterMassOutput = "waterMass";
    public const string WaterMassState = "waterMass";

    private readonly int _feedwaterIndex;
    private readonly int _heatIndex;
    private readonly int _massStateIndex;
    private readonly int _levelIndex;
    private readonly int _steamGenerationIndex;
    private readonly int _overflowIndex;
    private readonly int _massOutputIndex;
    private double _steamGenerationKgPerS;
    private double _overflowKgPerS;

    /// <param name="name">Nome da instância.</param>
    /// <param name="areaM2">Área da seção horizontal, em m² (&gt; 0).</param>
    /// <param name="heightM">Altura interna (diâmetro), em m (&gt; 0).</param>
    /// <param name="waterDensityKgPerM3">Densidade da água no tubulão, em kg/m³ (&gt; 0).</param>
    /// <param name="evaporationEnthalpyJPerKg">Entalpia para evaporar 1 kg de água de alimentação, em J/kg (&gt; 0).</param>
    /// <param name="initialLevelM">Nível inicial, em m (limitado a [0, altura]).</param>
    public Drum(
        string name,
        double areaM2,
        double heightM,
        double waterDensityKgPerM3,
        double evaporationEnthalpyJPerKg,
        double initialLevelM = 0.0)
        : base(name)
    {
        RequirePositive(areaM2, nameof(areaM2), "A área");
        RequirePositive(heightM, nameof(heightM), "A altura");
        RequirePositive(waterDensityKgPerM3, nameof(waterDensityKgPerM3), "A densidade");
        RequirePositive(evaporationEnthalpyJPerKg, nameof(evaporationEnthalpyJPerKg), "A entalpia de evaporação");

        AreaM2 = areaM2;
        HeightM = heightM;
        WaterDensityKgPerM3 = waterDensityKgPerM3;
        EvaporationEnthalpyJPerKg = evaporationEnthalpyJPerKg;

        _feedwaterIndex = Inputs.Add(FeedwaterFlowInput);
        _heatIndex = Inputs.Add(HeatInput);
        _massStateIndex = StateSignals.Add(WaterMassState, Math.Clamp(initialLevelM, 0.0, heightM) * areaM2 * waterDensityKgPerM3);
        _levelIndex = OutputSignals.Add(LevelOutput);
        _steamGenerationIndex = OutputSignals.Add(SteamGenerationOutput);
        _overflowIndex = OutputSignals.Add(OverflowOutput);
        _massOutputIndex = OutputSignals.Add(WaterMassOutput);

        ComputeOutputs();
    }

    public double AreaM2 { get; }

    public double HeightM { get; }

    public double WaterDensityKgPerM3 { get; }

    public double EvaporationEnthalpyJPerKg { get; }

    public double CapacityKg => AreaM2 * HeightM * WaterDensityKgPerM3;

    /// <summary>Vazão de água de alimentação, em kg/s. Valores negativos são tratados como zero.</summary>
    public double FeedwaterFlowKgPerS
    {
        get => Inputs[_feedwaterIndex];
        set => Inputs[_feedwaterIndex] = value;
    }

    /// <summary>Calor recebido da fornalha, em W. Valores negativos são tratados como zero.</summary>
    public double HeatW
    {
        get => Inputs[_heatIndex];
        set => Inputs[_heatIndex] = value;
    }

    public double WaterMassKg => States[_massStateIndex];

    public double LevelM => Outputs[_levelIndex];

    /// <summary>Vapor gerado (evaporação média no último passo), em kg/s.</summary>
    public double SteamGenerationKgPerS => Outputs[_steamGenerationIndex];

    /// <summary>Água que passou do topo (média no último passo), em kg/s.</summary>
    public double OverflowKgPerS => Outputs[_overflowIndex];

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        double feedwater = Math.Max(0.0, Inputs[_feedwaterIndex]);
        double evaporation = Math.Max(0.0, Inputs[_heatIndex]) / EvaporationEnthalpyJPerKg;

        double massBefore = StateSignals[_massStateIndex];
        double mass = massBefore + ((feedwater - evaporation) * stepSeconds);
        double overflowMass = 0.0;

        if (mass < 0.0)
        {
            // Secou no meio do passo: só evapora a água que havia mais a que entrou.
            evaporation = (massBefore / stepSeconds) + feedwater;
            mass = 0.0;
        }
        else if (mass > CapacityKg)
        {
            overflowMass = mass - CapacityKg;
            mass = CapacityKg;
        }

        _steamGenerationKgPerS = evaporation;
        _overflowKgPerS = overflowMass / stepSeconds;
        StateSignals[_massStateIndex] = mass;
    }

    protected override void ComputeOutputs()
    {
        double mass = StateSignals[_massStateIndex];
        OutputSignals[_levelIndex] = mass / (WaterDensityKgPerM3 * AreaM2);
        OutputSignals[_steamGenerationIndex] = _steamGenerationKgPerS;
        OutputSignals[_overflowIndex] = _overflowKgPerS;
        OutputSignals[_massOutputIndex] = mass;
    }

    private static void RequirePositive(double value, string parameterName, string label)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{label} deve ser finita e maior que zero.");
        }
    }
}
