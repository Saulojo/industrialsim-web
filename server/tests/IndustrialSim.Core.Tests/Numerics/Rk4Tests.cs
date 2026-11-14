using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Tests.Numerics;

public class Rk4Tests
{
    private const double Dt = 0.01;

    /// <summary>Critério 1 da Etapa 1: dy/dt = −y em t = 1 s, com dt = 0,01 s, coincide com e⁻¹ com erro &lt; 1e-8.</summary>
    [Fact]
    public void Scalar_ExponentialDecay_MatchesExpMinusOne()
    {
        double y = 1.0;
        for (int n = 0; n < 100; n++)
        {
            y = Rk4.Step((_, x) => -x, n * Dt, y, Dt);
        }

        double error = Math.Abs(y - Math.Exp(-1.0));
        Assert.True(error < 1e-8, $"erro = {error:E3}");
    }

    [Fact]
    public void Vector_ExponentialDecay_MatchesExpMinusOne()
    {
        var integrator = new Rk4Integrator(1);
        double[] y = [1.0];
        for (int n = 0; n < 100; n++)
        {
            integrator.Step((_, x, dx) => dx[0] = -x[0], n * Dt, y, Dt);
        }

        double error = Math.Abs(y[0] - Math.Exp(-1.0));
        Assert.True(error < 1e-8, $"erro = {error:E3}");
    }

    [Fact]
    public void ScalarAndVector_AreBitIdentical()
    {
        var integrator = new Rk4Integrator(1);
        double scalar = 0.3;
        double[] vector = [0.3];

        for (int n = 0; n < 1000; n++)
        {
            double t = n * Dt;
            scalar = Rk4.Step((time, x) => Math.Sin(time) - (0.7 * x * x), t, scalar, Dt);
            integrator.Step((time, x, dx) => dx[0] = Math.Sin(time) - (0.7 * x[0] * x[0]), t, vector, Dt);
        }

        Assert.Equal(BitConverter.DoubleToInt64Bits(scalar), BitConverter.DoubleToInt64Bits(vector[0]));
    }

    /// <summary>Oscilador harmônico x'' = −x: x(t) = cos t, v(t) = −sen t.</summary>
    [Fact]
    public void Vector_HarmonicOscillator_MatchesAnalyticSolution()
    {
        var integrator = new Rk4Integrator(2);
        double[] state = [1.0, 0.0];

        for (int n = 0; n < 1000; n++)
        {
            integrator.Step(
                (_, x, dx) =>
                {
                    dx[0] = x[1];
                    dx[1] = -x[0];
                },
                n * Dt,
                state,
                Dt);
        }

        Assert.True(Math.Abs(state[0] - Math.Cos(10.0)) < 1e-8);
        Assert.True(Math.Abs(state[1] + Math.Sin(10.0)) < 1e-8);
    }

    /// <summary>O tempo é passado certo aos estágios: dy/dt = t³ é integrado exatamente pelo RK4 (regra de Simpson).</summary>
    [Fact]
    public void Scalar_TimeDependentCubic_IsIntegratedExactly()
    {
        double y = 0.0;
        for (int n = 0; n < 100; n++)
        {
            y = Rk4.Step((t, _) => t * t * t, n * Dt, y, Dt);
        }

        Assert.True(Math.Abs(y - 0.25) < 1e-14, $"y = {y:R}");
    }

    /// <summary>Ordem 4: reduzir o passo à metade divide o erro global por cerca de 16.</summary>
    [Fact]
    public void Scalar_ConvergenceOrder_IsFour()
    {
        double coarse = ErrorAtOneSecond(steps: 10);
        double fine = ErrorAtOneSecond(steps: 20);

        double order = Math.Log2(coarse / fine);
        Assert.InRange(order, 3.8, 4.2);
    }

    [Fact]
    public void Vector_WrongDimension_Throws()
    {
        var integrator = new Rk4Integrator(2);

        Assert.Throws<ArgumentException>(() => integrator.Step((_, _, _) => { }, 0.0, new double[1], Dt));
    }

    private static double ErrorAtOneSecond(int steps)
    {
        double dt = 1.0 / steps;
        double y = 1.0;
        for (int n = 0; n < steps; n++)
        {
            y = Rk4.Step((_, x) => -x, n * dt, y, dt);
        }

        return Math.Abs(y - Math.Exp(-1.0));
    }
}
