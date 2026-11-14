namespace IndustrialSim.Core.Numerics;

/// <summary>
/// Lado direito de um sistema de EDOs dx/dt = f(t, x): lê <paramref name="state"/> e escreve em <paramref name="derivative"/>.
/// </summary>
public delegate void OdeSystem(double timeSeconds, ReadOnlySpan<double> state, Span<double> derivative);

/// <summary>
/// Runge-Kutta clássico de 4ª ordem, passo fixo (D06).
/// Fonte: Butcher, J. C. "Numerical Methods for Ordinary Differential Equations", 3ª ed., Wiley, 2016;
/// Press et al., "Numerical Recipes", 3ª ed., seção 17.1.
/// </summary>
/// <remarks>
/// As versões escalar e vetorial fazem as mesmas operações na mesma ordem, de modo que um sistema de
/// dimensão 1 gera resultado idêntico bit a bit nas duas (RNF02).
/// </remarks>
public static class Rk4
{
    /// <summary>Avança uma EDO escalar dy/dt = f(t, y) em um passo e devolve o novo y.</summary>
    public static double Step(Func<double, double, double> derivative, double timeSeconds, double state, double stepSeconds)
    {
        ArgumentNullException.ThrowIfNull(derivative);

        double half = stepSeconds / 2.0;
        double k1 = derivative(timeSeconds, state);
        double k2 = derivative(timeSeconds + half, state + (half * k1));
        double k3 = derivative(timeSeconds + half, state + (half * k2));
        double k4 = derivative(timeSeconds + stepSeconds, state + (stepSeconds * k3));

        return state + ((stepSeconds / 6.0) * (k1 + (2.0 * k2) + (2.0 * k3) + k4));
    }
}

/// <summary>
/// Integrador RK4 vetorial com buffers próprios: nenhum passo aloca memória.
/// Uma instância por sistema; não é seguro para uso simultâneo por várias threads.
/// </summary>
public sealed class Rk4Integrator
{
    private readonly double[] _k1;
    private readonly double[] _k2;
    private readonly double[] _k3;
    private readonly double[] _k4;
    private readonly double[] _stage;

    public Rk4Integrator(int dimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);

        Dimension = dimension;
        _k1 = new double[dimension];
        _k2 = new double[dimension];
        _k3 = new double[dimension];
        _k4 = new double[dimension];
        _stage = new double[dimension];
    }

    public int Dimension { get; }

    /// <summary>Avança <paramref name="state"/> em um passo, no próprio vetor.</summary>
    public void Step(OdeSystem system, double timeSeconds, Span<double> state, double stepSeconds)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (state.Length != Dimension)
        {
            throw new ArgumentException($"O estado tem {state.Length} elementos; o integrador espera {Dimension}.", nameof(state));
        }

        double half = stepSeconds / 2.0;

        system(timeSeconds, state, _k1);

        for (int i = 0; i < state.Length; i++)
        {
            _stage[i] = state[i] + (half * _k1[i]);
        }

        system(timeSeconds + half, _stage, _k2);

        for (int i = 0; i < state.Length; i++)
        {
            _stage[i] = state[i] + (half * _k2[i]);
        }

        system(timeSeconds + half, _stage, _k3);

        for (int i = 0; i < state.Length; i++)
        {
            _stage[i] = state[i] + (stepSeconds * _k3[i]);
        }

        system(timeSeconds + stepSeconds, _stage, _k4);

        double sixth = stepSeconds / 6.0;
        for (int i = 0; i < state.Length; i++)
        {
            state[i] += sixth * (_k1[i] + (2.0 * _k2[i]) + (2.0 * _k3[i]) + _k4[i]);
        }
    }
}
