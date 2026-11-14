namespace IndustrialSim.Core.Numerics;

/// <summary>
/// Modelo linear em espaço de estados, uma entrada e uma saída: dx/dt = A·x + B·u, y = C·x + D·u.
/// </summary>
public sealed class StateSpaceModel
{
    private readonly double[] _a; // n×n, por linhas
    private readonly double[] _b;
    private readonly double[] _c;

    /// <param name="a">Matriz A (n×n), por linhas: a[linha·n + coluna].</param>
    /// <param name="b">Vetor B (n).</param>
    /// <param name="c">Vetor C (n).</param>
    /// <param name="d">Ganho direto D.</param>
    public StateSpaceModel(ReadOnlySpan<double> a, ReadOnlySpan<double> b, ReadOnlySpan<double> c, double d)
    {
        int order = b.Length;
        if (order == 0)
        {
            throw new ArgumentException("O modelo precisa de pelo menos um estado.", nameof(b));
        }

        if (c.Length != order || a.Length != order * order)
        {
            throw new ArgumentException($"Dimensões incoerentes: A tem {a.Length} elementos, B tem {b.Length} e C tem {c.Length}.");
        }

        Order = order;
        _a = a.ToArray();
        _b = b.ToArray();
        _c = c.ToArray();
        D = d;
    }

    /// <summary>Número de estados.</summary>
    public int Order { get; }

    public ReadOnlySpan<double> A => _a;

    public ReadOnlySpan<double> B => _b;

    public ReadOnlySpan<double> C => _c;

    public double D { get; }

    /// <summary>Calcula dx/dt = A·x + B·u.</summary>
    public void Derivative(ReadOnlySpan<double> state, double input, Span<double> derivative)
    {
        for (int row = 0; row < Order; row++)
        {
            double sum = _b[row] * input;
            int offset = row * Order;
            for (int column = 0; column < Order; column++)
            {
                sum += _a[offset + column] * state[column];
            }

            derivative[row] = sum;
        }
    }

    /// <summary>Calcula y = C·x + D·u.</summary>
    public double Output(ReadOnlySpan<double> state, double input)
    {
        double sum = D * input;
        for (int i = 0; i < Order; i++)
        {
            sum += _c[i] * state[i];
        }

        return sum;
    }
}
