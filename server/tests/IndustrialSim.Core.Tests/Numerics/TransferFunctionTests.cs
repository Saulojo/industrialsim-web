using IndustrialSim.Core.Components;
using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Tests.Numerics;

/// <summary>
/// Critério 7 da Etapa 1: funções de transferência de 1ª e 2ª ordem convertidas para espaço de estados;
/// a resposta ao degrau coincide com a solução analítica (erro &lt; 1e-6).
/// As soluções analíticas são as de Ogata, "Modern Control Engineering", 5ª ed., seções 5-2 e 5-3.
/// </summary>
public class TransferFunctionTests(ITestOutputHelper output)
{
    private const double Dt = 0.01;
    private const double Tolerance = 1e-6;

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(2.5, 0.5)]
    [InlineData(-3.0, 12.0)]
    public void FirstOrder_StepResponse_MatchesAnalytic(double gain, double timeConstantSeconds)
    {
        var block = new StateSpaceBlock("G", TransferFunction.FirstOrder(gain, timeConstantSeconds));

        double maxError = MaxStepResponseError(
            block,
            seconds: 10.0 * timeConstantSeconds,
            analytic: t => gain * (1.0 - Math.Exp(-t / timeConstantSeconds)));

        output.WriteLine($"1ª ordem, K = {gain}, tau = {timeConstantSeconds} s: erro máximo = {maxError:E3}");
        Assert.True(maxError < Tolerance, $"erro máximo = {maxError:E3}");
    }

    /// <summary>Subamortecido (0 &lt; ζ &lt; 1): y = K·[1 − e^(−ζωn·t)·(cos ωd·t + ζ/√(1−ζ²)·sen ωd·t)], ωd = ωn·√(1−ζ²).</summary>
    [Theory]
    [InlineData(1.0, 2.0, 0.5)]
    [InlineData(3.0, 5.0, 0.1)]
    [InlineData(1.0, 0.5, 0.9)]
    public void SecondOrder_Underdamped_StepResponse_MatchesAnalytic(double gain, double wn, double zeta)
    {
        var block = new StateSpaceBlock("G", TransferFunction.SecondOrder(gain, wn, zeta));
        double wd = wn * Math.Sqrt(1.0 - (zeta * zeta));

        double maxError = MaxStepResponseError(
            block,
            seconds: 30.0,
            analytic: t => gain * (1.0 - (Math.Exp(-zeta * wn * t) * (Math.Cos(wd * t) + (zeta / Math.Sqrt(1.0 - (zeta * zeta)) * Math.Sin(wd * t))))));

        output.WriteLine($"2ª ordem subamortecida, K = {gain}, wn = {wn} rad/s, zeta = {zeta}: erro máximo = {maxError:E3}");
        Assert.True(maxError < Tolerance, $"erro máximo = {maxError:E3}");
    }

    /// <summary>Criticamente amortecido (ζ = 1): y = K·[1 − e^(−ωn·t)·(1 + ωn·t)].</summary>
    [Fact]
    public void SecondOrder_CriticallyDamped_StepResponse_MatchesAnalytic()
    {
        const double Gain = 2.0;
        const double Wn = 1.5;
        var block = new StateSpaceBlock("G", TransferFunction.SecondOrder(Gain, Wn, dampingRatio: 1.0));

        double maxError = MaxStepResponseError(
            block,
            seconds: 30.0,
            analytic: t => Gain * (1.0 - (Math.Exp(-Wn * t) * (1.0 + (Wn * t)))));

        output.WriteLine($"2ª ordem criticamente amortecida: erro máximo = {maxError:E3}");
        Assert.True(maxError < Tolerance, $"erro máximo = {maxError:E3}");
    }

    /// <summary>Superamortecido (ζ &gt; 1), polos reais −p₁ e −p₂: y = K·[1 − (p₂·e^(−p₁t) − p₁·e^(−p₂t))/(p₂ − p₁)].</summary>
    [Fact]
    public void SecondOrder_Overdamped_StepResponse_MatchesAnalytic()
    {
        const double Gain = 1.0;
        const double Wn = 2.0;
        const double Zeta = 2.0;
        var block = new StateSpaceBlock("G", TransferFunction.SecondOrder(Gain, Wn, Zeta));
        double root = Math.Sqrt((Zeta * Zeta) - 1.0);
        double p1 = Wn * (Zeta - root);
        double p2 = Wn * (Zeta + root);

        double maxError = MaxStepResponseError(
            block,
            seconds: 30.0,
            analytic: t => Gain * (1.0 - (((p2 * Math.Exp(-p1 * t)) - (p1 * Math.Exp(-p2 * t))) / (p2 - p1))));

        output.WriteLine($"2ª ordem superamortecida: erro máximo = {maxError:E3}");
        Assert.True(maxError < Tolerance, $"erro máximo = {maxError:E3}");
    }

    /// <summary>Com zero e ganho direto (avanço-atraso): G(s) = (2s + 1)/(5s + 1) → y = 1 − 0,6·e^(−t/5).</summary>
    [Fact]
    public void LeadLag_StepResponse_MatchesAnalytic()
    {
        var block = new StateSpaceBlock("G", new TransferFunction([2.0, 1.0], [5.0, 1.0]));

        double maxError = MaxStepResponseError(block, seconds: 50.0, analytic: t => 1.0 - (0.6 * Math.Exp(-t / 5.0)));

        Assert.Equal(0.4, block.Model.D, 15);
        Assert.True(maxError < Tolerance, $"erro máximo = {maxError:E3}");
    }

    [Fact]
    public void ToStateSpace_ProducesControllableCanonicalForm()
    {
        // G(s) = (4s + 6) / (2s² + 6s + 4) = (2s + 3) / (s² + 3s + 2)
        var model = new TransferFunction([4.0, 6.0], [2.0, 6.0, 4.0]).ToStateSpace();

        Assert.Equal(2, model.Order);
        Assert.Equal([0.0, 1.0, -2.0, -3.0], model.A.ToArray());
        Assert.Equal([0.0, 1.0], model.B.ToArray());
        Assert.Equal([3.0, 2.0], model.C.ToArray());
        Assert.Equal(0.0, model.D);
    }

    [Fact]
    public void LeadingZeros_AreIgnored()
    {
        var transferFunction = new TransferFunction([0.0, 0.0, 1.0], [0.0, 2.0, 1.0]);

        Assert.Equal(1, transferFunction.Order);
        Assert.Equal([1.0], transferFunction.Numerator.ToArray());
        Assert.Equal([2.0, 1.0], transferFunction.Denominator.ToArray());
    }

    [Fact]
    public void InvalidTransferFunctions_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new TransferFunction([1.0, 0.0, 0.0], [1.0, 1.0])); // imprópria
        Assert.Throws<ArgumentException>(() => new TransferFunction([1.0], [0.0, 0.0])); // denominador zero
        Assert.Throws<ArgumentException>(() => new TransferFunction([1.0], [2.0])); // ganho puro
        Assert.Throws<ArgumentException>(() => new TransferFunction([double.NaN], [1.0, 1.0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransferFunction.FirstOrder(1.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransferFunction.SecondOrder(1.0, 0.0, 0.5));
    }

    [Fact]
    public void StateSpaceModel_RejectsIncoherentDimensions()
    {
        Assert.Throws<ArgumentException>(() => new StateSpaceModel([1.0, 2.0, 3.0], [1.0, 0.0], [1.0, 0.0], 0.0));
        Assert.Throws<ArgumentException>(() => new StateSpaceModel([], [], [], 0.0));
    }

    /// <summary>Aplica um degrau unitário em t = 0 e devolve o maior erro absoluto contra a solução analítica, passo a passo.</summary>
    private static double MaxStepResponseError(StateSpaceBlock block, double seconds, Func<double, double> analytic)
    {
        block.Input = 1.0;
        double maxError = 0.0;
        int steps = (int)Math.Round(seconds / Dt);

        for (int n = 0; n < steps; n++)
        {
            block.Step(n * Dt, Dt);
            double time = (n + 1) * Dt;
            maxError = Math.Max(maxError, Math.Abs(block.Output - analytic(time)));
        }

        return maxError;
    }
}
