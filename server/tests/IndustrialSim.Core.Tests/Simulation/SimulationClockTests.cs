using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Core.Tests.Simulation;

public class SimulationClockTests
{
    [Fact]
    public void Default_IsTenMilliseconds()
    {
        var clock = new SimulationClock();

        Assert.Equal(0.01, clock.StepSeconds);
        Assert.Equal(0, clock.StepCount);
        Assert.Equal(0.0, clock.TimeSeconds);
    }

    /// <summary>Somar 0,01 um milhão de vezes acumula erro; passos × dt não.</summary>
    [Fact]
    public void Time_DoesNotAccumulateRoundingError()
    {
        var clock = new SimulationClock();
        double summed = 0.0;

        for (int i = 0; i < 1_000_000; i++)
        {
            clock.Advance();
            summed += 0.01;
        }

        Assert.Equal(1_000_000, clock.StepCount);
        Assert.Equal(10_000.0, clock.TimeSeconds);
        Assert.NotEqual(10_000.0, summed);
    }

    [Fact]
    public void Reset_GoesBackToZero()
    {
        var clock = new SimulationClock(0.5);
        clock.Advance();
        clock.Advance();
        Assert.Equal(1.0, clock.TimeSeconds);

        clock.Reset();

        Assert.Equal(0, clock.StepCount);
        Assert.Equal(0.0, clock.TimeSeconds);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidStep_IsRejected(double stepSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock(stepSeconds));
    }
}
