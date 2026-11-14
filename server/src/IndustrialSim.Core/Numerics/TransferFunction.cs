namespace IndustrialSim.Core.Numerics;

/// <summary>
/// Função de transferência racional G(s) = N(s)/D(s), com os coeficientes em potências decrescentes de s
/// (ex.: s² + 3s + 2 é [1, 3, 2]). Precisa ser própria: grau de N ≤ grau de D.
/// </summary>
public sealed class TransferFunction
{
    private readonly double[] _numerator;
    private readonly double[] _denominator;

    public TransferFunction(ReadOnlySpan<double> numerator, ReadOnlySpan<double> denominator)
    {
        foreach (double coefficient in numerator)
        {
            RequireFinite(coefficient, nameof(numerator));
        }

        foreach (double coefficient in denominator)
        {
            RequireFinite(coefficient, nameof(denominator));
        }

        _numerator = TrimLeadingZeros(numerator);
        _denominator = TrimLeadingZeros(denominator);

        if (_denominator.Length == 0)
        {
            throw new ArgumentException("O denominador não pode ser zero.", nameof(denominator));
        }

        if (_denominator.Length < 2)
        {
            throw new ArgumentException("O denominador precisa ter grau 1 ou maior (um ganho puro não tem estado).", nameof(denominator));
        }

        if (_numerator.Length > _denominator.Length)
        {
            throw new ArgumentException(
                $"Função de transferência imprópria: o numerador tem grau {_numerator.Length - 1} e o denominador, grau {_denominator.Length - 1}.",
                nameof(numerator));
        }
    }

    /// <summary>Coeficientes do numerador, potências decrescentes, sem zeros à esquerda.</summary>
    public ReadOnlySpan<double> Numerator => _numerator;

    /// <summary>Coeficientes do denominador, potências decrescentes, sem zeros à esquerda.</summary>
    public ReadOnlySpan<double> Denominator => _denominator;

    /// <summary>Grau do denominador = número de estados da realização.</summary>
    public int Order => _denominator.Length - 1;

    /// <summary>1ª ordem: G(s) = K / (τs + 1).</summary>
    public static TransferFunction FirstOrder(double gain, double timeConstantSeconds)
    {
        if (!(timeConstantSeconds > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(timeConstantSeconds), timeConstantSeconds, "A constante de tempo deve ser maior que zero.");
        }

        return new TransferFunction([gain], [timeConstantSeconds, 1.0]);
    }

    /// <summary>2ª ordem padrão: G(s) = K·ωn² / (s² + 2ζωn·s + ωn²).</summary>
    public static TransferFunction SecondOrder(double gain, double naturalFrequencyRadPerS, double dampingRatio)
    {
        if (!(naturalFrequencyRadPerS > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(naturalFrequencyRadPerS), naturalFrequencyRadPerS, "A frequência natural deve ser maior que zero.");
        }

        double wn2 = naturalFrequencyRadPerS * naturalFrequencyRadPerS;
        return new TransferFunction([gain * wn2], [1.0, 2.0 * dampingRatio * naturalFrequencyRadPerS, wn2]);
    }

    /// <summary>
    /// Converte para espaço de estados na forma canônica controlável.
    /// Com G(s) = (bₙsⁿ + … + b₀)/(sⁿ + aₙ₋₁sⁿ⁻¹ + … + a₀), já dividida pelo coeficiente líder do denominador:
    /// A é a matriz companheira (1 na superdiagonal; última linha −a₀ … −aₙ₋₁), B = [0 … 0 1]ᵀ,
    /// Cᵢ = bᵢ − aᵢ·bₙ e D = bₙ.
    /// Fonte: Ogata, K. "Modern Control Engineering", 5ª ed., Prentice Hall, 2010, seção 9-2.
    /// </summary>
    public StateSpaceModel ToStateSpace()
    {
        int n = Order;
        double leading = _denominator[0];

        // a[i] e b[i] multiplicam sⁱ, já normalizados; o numerador é completado com zeros até o grau n.
        double[] a = new double[n];
        double[] b = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            a[i] = _denominator[n - i] / leading;
        }

        for (int i = 0; i < _numerator.Length; i++)
        {
            b[i] = _numerator[_numerator.Length - 1 - i] / leading;
        }

        double[] matrixA = new double[n * n];
        double[] vectorB = new double[n];
        double[] vectorC = new double[n];

        for (int row = 0; row < n - 1; row++)
        {
            matrixA[(row * n) + row + 1] = 1.0;
        }

        for (int column = 0; column < n; column++)
        {
            matrixA[((n - 1) * n) + column] = -a[column];
            vectorC[column] = b[column] - (a[column] * b[n]);
        }

        vectorB[n - 1] = 1.0;

        return new StateSpaceModel(matrixA, vectorB, vectorC, b[n]);
    }

    private static void RequireFinite(double coefficient, string parameterName)
    {
        if (!double.IsFinite(coefficient))
        {
            throw new ArgumentException("Os coeficientes devem ser finitos.", parameterName);
        }
    }

    private static double[] TrimLeadingZeros(ReadOnlySpan<double> coefficients)
    {
        int start = 0;
        while (start < coefficients.Length && coefficients[start] == 0.0)
        {
            start++;
        }

        return coefficients[start..].ToArray();
    }
}
