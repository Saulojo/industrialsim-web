using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Components;

/// <summary>
/// Transmissor: transforma o valor real do processo no valor medido, com amortecimento, ruído e quantização.
/// 1. Amortecimento: filtro de 1ª ordem, τ·dy/dt = u − y (o "damping" de um transmissor; com τ = 0, não há filtro).
/// 2. Ruído: gaussiano de média zero somado à saída do filtro, com gerador próprio e semente fixa (D06, D22).
/// 3. Quantização: arredondamento ao múltiplo mais próximo da resolução (a resolução do conversor A/D).
/// Fonte: Seborg, Edgar, Mellichamp, Doyle. "Process Dynamics and Control", 4ª ed., Wiley, 2016, seção 9.1
/// (sensores e transmissores: dinâmica de 1ª ordem do elemento de medição).
/// </summary>
/// <remarks>
/// A saída está na mesma unidade da entrada (unidade de engenharia). A conversão para 4–20 mA e contagens,
/// com faixa LRV/URV, é da imagem de I/O (Etapa 4).
/// O ruído só existe na medição: nunca volta para a física, e o gerador não entra no hash de estado.
/// Com τ = 0 o transmissor não tem estado.
/// </remarks>
public sealed class Transmitter : Component
{
    public const string ValueInput = "value";
    public const string MeasurementOutput = "measurement";
    public const string FilteredState = "filtered";

    private readonly Func<double, double, double> _derivative;
    private readonly SeededRandom _random;
    private readonly int _valueIndex;
    private readonly int _measurementIndex;
    private readonly int _filteredIndex = -1;
    private readonly bool _hasInitialValue;
    private double _heldInput;
    private double _measurement;
    private bool _stepped;

    /// <param name="name">Nome da instância.</param>
    /// <param name="dampingSeconds">Constante de tempo do amortecimento, em segundos (≥ 0; 0 = sem filtro).</param>
    /// <param name="noiseStandardDeviation">Desvio padrão do ruído, na unidade da medição (≥ 0).</param>
    /// <param name="resolution">Passo de quantização, na unidade da medição (≥ 0; 0 = sem quantização).</param>
    /// <param name="noiseSeed">Semente do gerador de ruído.</param>
    /// <param name="initialValue">Valor inicial do filtro; se nulo, o filtro começa no valor de entrada.</param>
    public Transmitter(
        string name,
        double dampingSeconds = 0.0,
        double noiseStandardDeviation = 0.0,
        double resolution = 0.0,
        ulong noiseSeed = 0,
        double? initialValue = null)
        : base(name)
    {
        if (!double.IsFinite(dampingSeconds) || dampingSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(dampingSeconds), dampingSeconds, "O amortecimento deve ser finito e não negativo.");
        }

        if (!double.IsFinite(noiseStandardDeviation) || noiseStandardDeviation < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(noiseStandardDeviation), noiseStandardDeviation, "O desvio padrão do ruído deve ser finito e não negativo.");
        }

        if (!double.IsFinite(resolution) || resolution < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "A resolução deve ser finita e não negativa.");
        }

        DampingSeconds = dampingSeconds;
        NoiseStandardDeviation = noiseStandardDeviation;
        Resolution = resolution;
        _random = new SeededRandom(noiseSeed);
        _hasInitialValue = initialValue.HasValue;

        _valueIndex = Inputs.Add(ValueInput, initialValue ?? 0.0);
        if (dampingSeconds > 0.0)
        {
            _filteredIndex = StateSignals.Add(FilteredState, initialValue ?? 0.0);
        }

        _measurementIndex = OutputSignals.Add(MeasurementOutput);
        _derivative = Derivative;

        ComputeOutputs();
    }

    public double DampingSeconds { get; }

    public double NoiseStandardDeviation { get; }

    public double Resolution { get; }

    /// <summary>Valor real do processo.</summary>
    public double Value
    {
        get => Inputs[_valueIndex];
        set => Inputs[_valueIndex] = value;
    }

    /// <summary>Valor medido: filtrado, com ruído e quantizado.</summary>
    public double Measurement => Outputs[_measurementIndex];

    protected override void OnInitialize()
    {
        // Sem valor inicial explícito, o filtro começa em regime com a entrada: a medição não "sobe do zero".
        if (_filteredIndex >= 0 && !_hasInitialValue)
        {
            StateSignals[_filteredIndex] = Inputs[_valueIndex];
        }
    }

    protected override void Advance(double timeSeconds, double stepSeconds)
    {
        double clean;
        if (_filteredIndex >= 0)
        {
            FirstOrderActuator.EnsureStable(Name, DampingSeconds, stepSeconds);
            _heldInput = Inputs[_valueIndex];
            clean = Rk4.Step(_derivative, timeSeconds, StateSignals[_filteredIndex], stepSeconds);
            StateSignals[_filteredIndex] = clean;
        }
        else
        {
            clean = Inputs[_valueIndex];
        }

        // Um sorteio por passo, sempre no passo: a sequência do ruído não depende de quantas vezes a saída é lida.
        double noisy = NoiseStandardDeviation > 0.0 ? clean + _random.NextGaussian(0.0, NoiseStandardDeviation) : clean;
        _measurement = Quantize(noisy);
        _stepped = true;
    }

    protected override void ComputeOutputs()
    {
        // Antes do primeiro passo ainda não houve sorteio: a medição é o valor limpo.
        OutputSignals[_measurementIndex] = _stepped
            ? _measurement
            : Quantize(_filteredIndex >= 0 ? StateSignals[_filteredIndex] : Inputs[_valueIndex]);
    }

    private double Quantize(double value) =>
        Resolution > 0.0 ? Math.Round(value / Resolution, MidpointRounding.AwayFromZero) * Resolution : value;

    private double Derivative(double timeSeconds, double filtered) => (_heldInput - filtered) / DampingSeconds;
}
