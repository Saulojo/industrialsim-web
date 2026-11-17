using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Sessions.Tests;

public class DurationHistogramTests
{
    [Fact]
    public void Percentiles_AreTheUpperEdgeOfTheBucket_AndNeverExceedTheMax()
    {
        var histogram = new DurationHistogram();
        for (int i = 1; i <= 100; i++)
        {
            histogram.Record(i * 100.0); // 0,1 ms a 10 ms
        }

        DurationSummary summary = histogram.Summarize();

        Assert.Equal(100, summary.Count);
        Assert.Equal(5.05, summary.MeanMs, 9);
        Assert.Equal(5.01, summary.P50Ms, 9);
        Assert.Equal(9.91, summary.P99Ms, 9);
        Assert.Equal(10.0, summary.MaxMs, 9);
    }

    [Fact]
    public void ValuesAbove100ms_CountInTheMaxAndTheTail()
    {
        var histogram = new DurationHistogram();
        for (int i = 0; i < 98; i++)
        {
            histogram.Record(50.0);
        }

        histogram.Record(250_000.0);
        histogram.Record(300_000.0);

        DurationSummary summary = histogram.Summarize();
        Assert.Equal(0.06, summary.P50Ms, 9);
        Assert.Equal(300.0, summary.P99Ms, 9);
        Assert.Equal(300.0, summary.MaxMs, 9);
    }

    [Fact]
    public void AddTo_MergesCountsAndMax()
    {
        var a = new DurationHistogram();
        var b = new DurationHistogram();
        a.Record(1000.0);
        b.Record(3000.0);

        var total = new DurationHistogram();
        a.AddTo(total);
        b.AddTo(total);

        DurationSummary summary = total.Summarize();
        Assert.Equal(2, summary.Count);
        Assert.Equal(2.0, summary.MeanMs, 9);
        Assert.Equal(3.0, summary.MaxMs, 9);
    }

    [Fact]
    public void Empty_IsAllZeros() => Assert.Equal(DurationSummary.Empty, new DurationHistogram().Summarize());
}
