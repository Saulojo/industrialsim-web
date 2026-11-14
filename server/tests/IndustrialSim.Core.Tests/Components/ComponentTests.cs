using IndustrialSim.Core.Components;
using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Core.Tests.Components;

public class ComponentTests
{
    [Fact]
    public void Signals_AreDeclaredByName()
    {
        var integrator = new TestIntegrator("INT-1");

        Assert.Equal("INT-1", integrator.Name);
        Assert.Equal(["rate"], integrator.Inputs.Names);
        Assert.Equal(["value", "doubled"], integrator.Outputs.Names);
        Assert.Equal(["value"], integrator.States.Names);
        Assert.True(integrator.Outputs.Contains("doubled"));
        Assert.Equal(1, integrator.Outputs.IndexOf("doubled"));
    }

    [Fact]
    public void Step_ReadsInputsAndUpdatesStateAndOutputs()
    {
        var integrator = new TestIntegrator("INT-1");
        integrator.Inputs["rate"] = 2.0;

        integrator.Step(0.0, 0.5);

        Assert.Equal(1.0, integrator.States["value"]);
        Assert.Equal(1.0, integrator.Outputs["value"]);
        Assert.Equal(2.0, integrator.Outputs["doubled"]);
    }

    [Fact]
    public void SetState_ReplacesStateAndRefreshesOutputs()
    {
        var integrator = new TestIntegrator("INT-1");

        integrator.SetState([4.0]);

        Assert.Equal(4.0, integrator.States[0]);
        Assert.Equal(8.0, integrator.Outputs["doubled"]);
        Assert.Throws<ArgumentException>(() => integrator.SetState([1.0, 2.0]));
    }

    [Fact]
    public void UnknownSignal_ErrorNamesComponentAndAvailableSignals()
    {
        var integrator = new TestIntegrator("INT-1");

        var error = Assert.Throws<KeyNotFoundException>(() => integrator.Outputs["nope"]);

        Assert.Contains("INT-1", error.Message, StringComparison.Ordinal);
        Assert.Contains("nope", error.Message, StringComparison.Ordinal);
        Assert.Contains("doubled", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateSignal_IsRejected()
    {
        var signals = new SignalSet("X", "entrada");
        signals.Add("a");

        Assert.Throws<ArgumentException>(() => signals.Add("a"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    public void InvalidStep_IsRejected(double stepSeconds)
    {
        var integrator = new TestIntegrator("INT-1");

        Assert.Throws<ArgumentOutOfRangeException>(() => integrator.Step(0.0, stepSeconds));
    }

    [Fact]
    public void StateHash_CoversStateOnly()
    {
        var a = new TestIntegrator("A");
        var b = new TestIntegrator("B");
        a.SetState([3.0]);
        b.SetState([3.0]);
        b.Inputs["rate"] = 99.0; // entrada diferente, estado igual

        Assert.Equal(HashOf(a), HashOf(b));

        b.Step(0.0, 0.01);

        Assert.NotEqual(HashOf(a), HashOf(b));
    }

    private static ulong HashOf(Component component)
    {
        var hasher = default(StateHasher);
        component.AddStateTo(ref hasher);
        return hasher.Value;
    }

    /// <summary>Componente mínimo para testar a base: integra a entrada "rate" (Euler basta aqui).</summary>
    private sealed class TestIntegrator : Component
    {
        public TestIntegrator(string name)
            : base(name)
        {
            Inputs.Add("rate");
            StateSignals.Add("value");
            OutputSignals.Add("value");
            OutputSignals.Add("doubled");
        }

        protected override void Advance(double timeSeconds, double stepSeconds) =>
            StateSignals[0] += Inputs[0] * stepSeconds;

        protected override void ComputeOutputs()
        {
            OutputSignals[0] = StateSignals[0];
            OutputSignals[1] = 2.0 * StateSignals[0];
        }
    }
}
