using IndustrialSim.Core.Components;

namespace IndustrialSim.Core.Tests.Components;

/// <summary>Transmissor, chaves de nível e pressão e válvula de alívio.</summary>
public class InstrumentTests(ITestOutputHelper output)
{
    private const double Dt = 0.01;

    /// <summary>Amortecimento: a medição chega a 63,2% do degrau em t = τ.</summary>
    [Fact]
    public void Transmitter_DampingIsFirstOrder()
    {
        var transmitter = new Transmitter("LT-1", dampingSeconds: 2.0, initialValue: 0.0) { Value = 1.0 };

        for (int n = 0; n < 200; n++)
        {
            transmitter.Step(n * Dt, Dt);
        }

        double expected = 1.0 - Math.Exp(-1.0);
        Assert.True(Math.Abs(transmitter.Measurement - expected) < 0.005 * expected, $"medição = {transmitter.Measurement}");
    }

    /// <summary>Ruído gaussiano de média zero com o desvio pedido; mesma semente, mesma sequência; outra semente, outra.</summary>
    [Fact]
    public void Transmitter_NoiseHasRequestedStatisticsAndIsReproducible()
    {
        const int Steps = 100_000;
        var first = new Transmitter("PT-1", noiseStandardDeviation: 0.5, noiseSeed: 11) { Value = 10.0 };
        var same = new Transmitter("PT-1", noiseStandardDeviation: 0.5, noiseSeed: 11) { Value = 10.0 };
        var other = new Transmitter("PT-1", noiseStandardDeviation: 0.5, noiseSeed: 12) { Value = 10.0 };

        double sum = 0.0;
        double sumSquares = 0.0;
        int different = 0;
        for (int n = 0; n < Steps; n++)
        {
            first.Step(n * Dt, Dt);
            same.Step(n * Dt, Dt);
            other.Step(n * Dt, Dt);

            Assert.Equal(first.Measurement, same.Measurement);
            different += first.Measurement != other.Measurement ? 1 : 0;
            double noise = first.Measurement - 10.0;
            sum += noise;
            sumSquares += noise * noise;
        }

        double mean = sum / Steps;
        double std = Math.Sqrt((sumSquares / Steps) - (mean * mean));
        output.WriteLine($"ruído: média = {mean:E3}; desvio = {std:F5} (pedido 0,5)");
        Assert.InRange(std, 0.49, 0.51);
        Assert.InRange(mean, -0.01, 0.01);
        Assert.Equal(Steps, different);
    }

    /// <summary>Ler a saída várias vezes não consome ruído: há um sorteio por passo.</summary>
    [Fact]
    public void Transmitter_ReadingOutputDoesNotAdvanceNoise()
    {
        var read = new Transmitter("PT-1", noiseStandardDeviation: 0.1, noiseSeed: 3) { Value = 1.0 };
        var notRead = new Transmitter("PT-1", noiseStandardDeviation: 0.1, noiseSeed: 3) { Value = 1.0 };

        for (int n = 0; n < 100; n++)
        {
            read.Step(n * Dt, Dt);
            read.UpdateOutputs();
            read.UpdateOutputs();
            notRead.Step(n * Dt, Dt);
            Assert.Equal(notRead.Measurement, read.Measurement);
        }
    }

    /// <summary>Quantização: a medição é sempre múltiplo da resolução, a no máximo meia resolução do valor real.</summary>
    [Fact]
    public void Transmitter_QuantizesToResolution()
    {
        var transmitter = new Transmitter("LT-1", resolution: 0.05);

        for (int n = 0; n < 1000; n++)
        {
            transmitter.Value = 0.3 + (0.001 * n);
            transmitter.Step(n * Dt, Dt);

            double steps = transmitter.Measurement / 0.05;
            Assert.Equal(Math.Round(steps), steps, 9);
            Assert.True(Math.Abs(transmitter.Measurement - transmitter.Value) <= 0.025 + 1e-12);
        }
    }

    [Fact]
    public void Transmitter_WithoutEffects_IsTransparentAndHasNoState()
    {
        var transmitter = new Transmitter("LT-1") { Value = 3.25 };

        transmitter.Step(0.0, Dt);

        Assert.Equal(3.25, transmitter.Measurement);
        Assert.Equal(0, transmitter.States.Count);
    }

    /// <summary>Chave de alta com banda morta: atua no ajuste, só desatua abaixo de ajuste − banda morta.</summary>
    [Fact]
    public void HighSwitch_HasDeadband()
    {
        var high = new ThresholdSwitch("LSHH-1", setpoint: 1.0, SwitchDirection.Rising, deadband: 0.1);

        Assert.False(Apply(high, 0.99));
        Assert.True(Apply(high, 1.0));
        Assert.True(Apply(high, 0.95)); // dentro da banda morta: continua atuada
        Assert.True(Apply(high, 0.9001));
        Assert.False(Apply(high, 0.9));
        Assert.False(Apply(high, 0.95)); // subindo dentro da banda morta: continua desatuada
    }

    /// <summary>Chave de baixa com banda morta: atua no ajuste, só desatua acima de ajuste + banda morta.</summary>
    [Fact]
    public void LowSwitch_HasDeadband()
    {
        var low = new ThresholdSwitch("LSLL-1", setpoint: 0.2, SwitchDirection.Falling, deadband: 0.05);

        Assert.False(Apply(low, 0.21));
        Assert.True(Apply(low, 0.2));
        Assert.True(Apply(low, 0.24));
        Assert.False(Apply(low, 0.25));
        Assert.False(Apply(low, 0.22));
    }

    [Fact]
    public void Switch_WithoutDeadband_ComparesWithSetpoint()
    {
        var high = new ThresholdSwitch("PSH-1", setpoint: 5.0, SwitchDirection.Rising);

        Assert.True(Apply(high, 5.0));
        Assert.False(Apply(high, 4.999));
        Assert.True(Apply(high, 6.0));
    }

    /// <summary>A válvula de alívio abre na pressão de ajuste e só fecha na de fechamento (blowdown).</summary>
    [Fact]
    public void ReliefValve_PopsAtSetAndReseatsBelow()
    {
        const double Atm = PhysicalConstants.StandardAtmospherePa;
        var relief = new ReliefValve("PSV-1", setPressurePa: 1.3e6, reseatPressurePa: 1.25e6, ratedFlowKgPerS: 5.0);

        Assert.False(Apply(relief, 1.29e6));
        Assert.Equal(0.0, relief.FlowKgPerS);

        Assert.True(Apply(relief, 1.3e6));
        Assert.Equal(5.0, relief.FlowKgPerS, 12); // vazão nominal na pressão de ajuste

        Assert.True(Apply(relief, 1.27e6)); // entre fechamento e ajuste: continua aberta
        Assert.Equal(5.0 * (1.27e6 - Atm) / (1.3e6 - Atm), relief.FlowKgPerS, 12);

        Assert.False(Apply(relief, 1.25e6));
        Assert.Equal(0.0, relief.FlowKgPerS);
        Assert.False(Apply(relief, 1.28e6)); // subindo de novo, só abre no ajuste
    }

    [Fact]
    public void InvalidParameters_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Transmitter("LT-1", dampingSeconds: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Transmitter("LT-1", noiseStandardDeviation: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdSwitch("LS-1", 1.0, SwitchDirection.Rising, deadband: -0.1));
        Assert.Throws<ArgumentException>(() => new ReliefValve("PSV-1", setPressurePa: 1.0e6, reseatPressurePa: 1.1e6, ratedFlowKgPerS: 1.0));
        Assert.Throws<ArgumentException>(() => new ReliefValve("PSV-1", setPressurePa: 1.0e6, reseatPressurePa: 5.0e4, ratedFlowKgPerS: 1.0));
    }

    private static bool Apply(ThresholdSwitch processSwitch, double value)
    {
        processSwitch.Value = value;
        processSwitch.Step(0.0, Dt);
        return processSwitch.IsActive;
    }

    private static bool Apply(ReliefValve relief, double pressurePa)
    {
        relief.PressurePa = pressurePa;
        relief.Step(0.0, Dt);
        return relief.IsOpen;
    }
}
