using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Core.Tests.Simulation;

public class StateHasherTests
{
    /// <summary>Vetores de teste publicados do FNV-1a de 64 bits: entrada vazia e "a".</summary>
    [Fact]
    public void Fnv1a_MatchesReferenceVectors()
    {
        var empty = default(StateHasher);
        Assert.Equal(0xCBF29CE484222325UL, empty.Value);

        var a = default(StateHasher);
        a.Add("a"u8);
        Assert.Equal(0xAF63DC4C8601EC8CUL, a.Value);
        Assert.Equal("af63dc4c8601ec8c", a.ToHex());
    }

    [Fact]
    public void SameValues_SameHash()
    {
        double[] state = [1.5, -2.25, 1e-300, double.MaxValue];
        double[] copy = [.. state];

        Assert.Equal(StateHasher.Hash(state), StateHasher.Hash(copy));
    }

    [Fact]
    public void OneBitOfDifference_ChangesHash()
    {
        double value = 0.1 + 0.2;
        double next = Math.BitIncrement(value);

        Assert.NotEqual(StateHasher.Hash([value]), StateHasher.Hash([next]));
    }

    [Fact]
    public void Order_ChangesHash()
    {
        Assert.NotEqual(StateHasher.Hash([1.0, 2.0]), StateHasher.Hash([2.0, 1.0]));
    }

    [Fact]
    public void NegativeZero_DiffersFromPositiveZero()
    {
        Assert.NotEqual(StateHasher.Hash([0.0]), StateHasher.Hash([-0.0]));
    }

    [Fact]
    public void IncrementalAndBatch_AreEquivalent()
    {
        var incremental = default(StateHasher);
        incremental.Add(1.0);
        incremental.Add(2.0);

        Assert.Equal(StateHasher.Hash([1.0, 2.0]), incremental.Value);
    }

    [Fact]
    public void MixedTypes_AreHashed()
    {
        var a = default(StateHasher);
        a.Add(10L);
        a.Add(true);

        var b = default(StateHasher);
        b.Add(10L);
        b.Add(false);

        Assert.NotEqual(a.Value, b.Value);
    }
}
