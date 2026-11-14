using IndustrialSim.Core.Components;

namespace IndustrialSim.Core.Tests.Components;

public class FirstOrderActuatorTests(ITestOutputHelper output)
{
    private const double Dt = 0.01;

    /// <summary>Critério 2 da Etapa 1: o atuador chega a 63,2% do degrau em t = τ (erro &lt; 0,5%).</summary>
    [Theory]
    [InlineData(0.1)]
    [InlineData(1.0)]
    [InlineData(5.0)]
    [InlineData(30.0)]
    public void StepResponse_Reaches63PercentAtTimeConstant(double timeConstantSeconds)
    {
        var actuator = new FirstOrderActuator("FV-101", timeConstantSeconds);
        actuator.Target = 1.0;

        int steps = (int)Math.Round(timeConstantSeconds / Dt);
        for (int n = 0; n < steps; n++)
        {
            actuator.Step(n * Dt, Dt);
        }

        double errorVersusCriterion = Math.Abs(actuator.Position - 0.632) / 0.632;
        double errorVersusExact = Math.Abs(actuator.Position - (1.0 - Math.Exp(-1.0)));
        output.WriteLine($"tau = {timeConstantSeconds} s: posição em t = tau = {actuator.Position:R}; erro vs. 63,2% = {errorVersusCriterion:P4}; erro vs. 1 - 1/e = {errorVersusExact:E3}");

        Assert.True(errorVersusCriterion < 0.005, $"erro = {errorVersusCriterion:P4}");
        // Além do critério: o erro do RK4 cresce com (dt/τ)⁴; no pior caso testado (dt/τ = 0,1) fica em ~3e-7.
        Assert.True(errorVersusExact < 1e-6, $"erro vs. solução exata = {errorVersusExact:E3}");
    }

    [Fact]
    public void Position_ConvergesToTargetWithoutOvershoot()
    {
        var actuator = new FirstOrderActuator("FV-101", timeConstantSeconds: 2.0);
        actuator.Target = 0.75;

        double previous = actuator.Position;
        for (int n = 0; n < 3000; n++)
        {
            actuator.Step(n * Dt, Dt);
            Assert.True(actuator.Position >= previous && actuator.Position <= 0.75);
            previous = actuator.Position;
        }

        Assert.True(Math.Abs(actuator.Position - 0.75) < 1e-6);
    }

    [Fact]
    public void Target_IsLimitedToTravel()
    {
        var actuator = new FirstOrderActuator("FV-101", timeConstantSeconds: 0.5, minimum: 0.0, maximum: 1.0, initialPosition: 0.5);

        actuator.Target = 7.0;
        Run(actuator, seconds: 20.0);
        Assert.True(actuator.Position <= 1.0);
        Assert.True(Math.Abs(actuator.Position - 1.0) < 1e-9);

        actuator.Target = -3.0;
        Run(actuator, seconds: 20.0);
        Assert.True(actuator.Position >= 0.0);
        Assert.True(Math.Abs(actuator.Position) < 1e-9);
    }

    [Fact]
    public void InitialPosition_IsHeldUntilCommanded()
    {
        var actuator = new FirstOrderActuator("FV-101", timeConstantSeconds: 1.0, initialPosition: 0.4);

        Run(actuator, seconds: 5.0);

        Assert.Equal(0.4, actuator.Position);
    }

    [Fact]
    public void StepTooLargeForTimeConstant_IsRejected()
    {
        var actuator = new FirstOrderActuator("FV-101", timeConstantSeconds: 0.001);

        var error = Assert.Throws<InvalidOperationException>(() => actuator.Step(0.0, Dt));
        Assert.Contains("FV-101", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void InvalidTimeConstant_IsRejected(double timeConstantSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirstOrderActuator("FV-101", timeConstantSeconds));
    }

    [Fact]
    public void InvertedLimits_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new FirstOrderActuator("FV-101", 1.0, minimum: 1.0, maximum: 0.0));
    }

    private static void Run(FirstOrderActuator actuator, double seconds)
    {
        int steps = (int)Math.Round(seconds / Dt);
        for (int n = 0; n < steps; n++)
        {
            actuator.Step(n * Dt, Dt);
        }
    }
}
