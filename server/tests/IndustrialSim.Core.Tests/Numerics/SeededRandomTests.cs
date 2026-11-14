using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Tests.Numerics;

public class SeededRandomTests
{
    /// <summary>
    /// Vetores obtidos seguindo à mão o código de referência do xoshiro256** com estado (1, 2, 3, 4):
    /// 1ª saída = rotl(2·5, 7)·9 = 11520; na 2ª, s1 = 0, então a saída é 0.
    /// </summary>
    [Fact]
    public void Xoshiro_KnownState_ProducesReferenceSequence()
    {
        var random = SeededRandom.FromState(1, 2, 3, 4);

        Assert.Equal(11520UL, random.NextUInt64());
        Assert.Equal(0UL, random.NextUInt64());
        Assert.Equal(1509978240UL, random.NextUInt64());
        Assert.Equal(1215971899390074240UL, random.NextUInt64());
    }

    /// <summary>SplitMix64 com semente 1234567: valores de referência publicados (Rosetta Code, "Pseudo-random numbers/Splitmix64").</summary>
    [Fact]
    public void Seed_IsExpandedBySplitMix64()
    {
        var state = new SeededRandom(1234567).GetState();

        Assert.Equal(6457827717110365317UL, state.S0);
        Assert.Equal(3203168211198807973UL, state.S1);
        Assert.Equal(9817491932198370423UL, state.S2);
        Assert.Equal(4593380528125082431UL, state.S3);
    }

    [Fact]
    public void SameSeed_SameSequence()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void DifferentSeed_DifferentSequence()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(43);

        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void SavedState_ResumesSequence()
    {
        var original = new SeededRandom(7);
        original.NextUInt64();
        var (s0, s1, s2, s3) = original.GetState();
        var resumed = SeededRandom.FromState(s0, s1, s2, s3);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(original.NextUInt64(), resumed.NextUInt64());
        }
    }

    [Fact]
    public void AllZeroState_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => SeededRandom.FromState(0, 0, 0, 0));
    }

    [Fact]
    public void NextDouble_StaysInUnitIntervalWithUniformMoments()
    {
        var random = new SeededRandom(2026);
        const int Count = 200_000;
        double sum = 0.0;
        double sumSquares = 0.0;

        for (int i = 0; i < Count; i++)
        {
            double u = random.NextDouble();
            Assert.True(u >= 0.0 && u < 1.0, $"u = {u:R}");
            sum += u;
            sumSquares += u * u;
        }

        double mean = sum / Count;
        double variance = (sumSquares / Count) - (mean * mean);
        Assert.InRange(mean, 0.495, 0.505); // esperado 1/2
        Assert.InRange(variance, 0.0823, 0.0843); // esperado 1/12 ≈ 0,0833
    }

    [Fact]
    public void NextGaussian_HasRequestedMeanAndDeviation()
    {
        var random = new SeededRandom(2026);
        const int Count = 200_000;
        double sum = 0.0;
        double sumSquares = 0.0;

        for (int i = 0; i < Count; i++)
        {
            double x = random.NextGaussian(mean: 3.0, standardDeviation: 0.5);
            Assert.True(double.IsFinite(x));
            sum += x;
            sumSquares += x * x;
        }

        double mean = sum / Count;
        double deviation = Math.Sqrt((sumSquares / Count) - (mean * mean));
        Assert.InRange(mean, 2.99, 3.01);
        Assert.InRange(deviation, 0.49, 0.51);
    }
}
