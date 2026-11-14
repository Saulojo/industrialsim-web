using IndustrialSim.Core.Components;

namespace IndustrialSim.Core.Tests.Components;

public class PumpTests
{
    private const double Dt = 0.01;
    private const double RatedFlow = 0.02;

    [Fact]
    public void NeverCommanded_StaysOff()
    {
        var pump = new Pump("P-101", RatedFlow);

        Run(pump, seconds: 60.0);

        Assert.Equal(0.0, pump.FlowM3PerS);
        Assert.False(pump.IsRunning);
    }

    [Fact]
    public void RunCommand_BringsFlowToRated()
    {
        var pump = new Pump("P-101", RatedFlow, timeConstantSeconds: 1.0);
        pump.Run = true;

        Run(pump, seconds: 20.0);

        Assert.True(Math.Abs(pump.FlowM3PerS - RatedFlow) < 1e-9);
        Assert.True(pump.FlowM3PerS <= RatedFlow);
        Assert.True(pump.IsRunning);
    }

    /// <summary>
    /// Regressão do protótipo, que forçava a bomba ligada a cada passo: aqui, desligar a bomba
    /// faz a vazão cair a zero e ela continua desligada.
    /// </summary>
    [Fact]
    public void StopCommand_BringsFlowBackToZeroAndKeepsItThere()
    {
        var pump = new Pump("P-101", RatedFlow, timeConstantSeconds: 1.0);
        pump.Run = true;
        Run(pump, seconds: 20.0);

        pump.Run = false;
        Run(pump, seconds: 30.0);

        Assert.False(pump.Run);
        Assert.True(pump.FlowM3PerS < 1e-12);
        Assert.False(pump.IsRunning);
    }

    [Fact]
    public void RunningFeedback_FollowsSpeedThreshold()
    {
        var pump = new Pump("P-101", RatedFlow, timeConstantSeconds: 1.0, runningThreshold: 0.5);
        pump.Run = true;

        // A rotação passa de 50% em t = τ·ln 2 ≈ 0,693 s.
        Run(pump, seconds: 0.6);
        Assert.False(pump.IsRunning);

        Run(pump, seconds: 0.2);
        Assert.True(pump.IsRunning);
    }

    [Fact]
    public void RunInput_IsTreatedAsBoolean()
    {
        var pump = new Pump("P-101", RatedFlow);

        pump.Inputs[Pump.RunInput] = 0.49;
        Run(pump, seconds: 20.0);
        Assert.Equal(0.0, pump.FlowM3PerS);

        pump.Inputs[Pump.RunInput] = 27648.0;
        Run(pump, seconds: 20.0);
        Assert.True(Math.Abs(pump.FlowM3PerS - RatedFlow) < 1e-9);
    }

    [Fact]
    public void InvalidParameters_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pump("P-101", -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pump("P-101", RatedFlow, timeConstantSeconds: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pump("P-101", RatedFlow, runningThreshold: 1.0));
    }

    private static void Run(Pump pump, double seconds)
    {
        int steps = (int)Math.Round(seconds / Dt);
        for (int n = 0; n < steps; n++)
        {
            pump.Step(n * Dt, Dt);
        }
    }
}
