using IndustrialSim.Core.Components;

namespace IndustrialSim.Core.Tests.Components;

/// <summary>Tubulão v1, queimador e modelo de pressão v1.</summary>
public class BoilerComponentTests(ITestOutputHelper output)
{
    private const double Dt = 0.01;
    private const double Atm = PhysicalConstants.StandardAtmospherePa;

    /// <summary>Balanço de massa do tubulão: ΔM = ∫(ṁ_água − ṁ_evap)dt, com entradas que variam.</summary>
    [Fact]
    public void Drum_MassChangeEqualsFeedwaterMinusEvaporation()
    {
        var drum = new Drum("B-1", areaM2: 4.8, heightM: 1.2, waterDensityKgPerM3: 887.0, evaporationEnthalpyJPerKg: 2.358e6, initialLevelM: 0.6);
        double initialMass = drum.WaterMassKg;
        double feedwaterIntegral = 0.0;
        double steamIntegral = 0.0;

        for (int n = 0; n < 60_000; n++)
        {
            int segment = n / 3000;
            drum.FeedwaterFlowKgPerS = 2.0 + (1.5 * Math.Sin(segment));
            drum.HeatW = 2.358e6 * (2.0 + (1.0 * Math.Cos(2.0 * segment)));
            drum.Step(n * Dt, Dt);

            feedwaterIntegral += drum.FeedwaterFlowKgPerS * Dt;
            steamIntegral += drum.SteamGenerationKgPerS * Dt;
            Assert.Equal(0.0, drum.OverflowKgPerS);
        }

        double change = drum.WaterMassKg - initialMass;
        double relativeError = Math.Abs(change - (feedwaterIntegral - steamIntegral)) / Math.Abs(change);
        output.WriteLine($"ΔM = {change:R} kg; ∫(água − vapor)dt = {feedwaterIntegral - steamIntegral:R} kg; erro relativo = {relativeError:E3}");
        Assert.True(Math.Abs(change) > 10.0);
        Assert.True(relativeError < 1e-9, $"erro relativo = {relativeError:E3}");
        Assert.Equal(drum.WaterMassKg / (887.0 * 4.8), drum.LevelM, 12);
    }

    /// <summary>Sem água não há evaporação: o tubulão seca, a massa para em zero e o vapor gerado cai a zero.</summary>
    [Fact]
    public void Drum_RunsDryWithoutGoingNegative()
    {
        var drum = new Drum("B-1", areaM2: 1.0, heightM: 1.0, waterDensityKgPerM3: 900.0, evaporationEnthalpyJPerKg: 2.0e6, initialLevelM: 0.01);
        drum.HeatW = 4.0e6; // 2 kg/s de evaporação, sem água de alimentação
        double evaporated = 0.0;

        for (int n = 0; n < 1000; n++)
        {
            drum.Step(n * Dt, Dt);
            evaporated += drum.SteamGenerationKgPerS * Dt;
            Assert.True(drum.WaterMassKg >= 0.0);
        }

        Assert.Equal(0.0, drum.WaterMassKg);
        Assert.Equal(0.0, drum.SteamGenerationKgPerS);
        Assert.Equal(9.0, evaporated, 9); // só evaporou a água que havia: 0,01 m × 1 m² × 900 kg/m³
    }

    /// <summary>Seco e com água entrando, evapora exatamente o que entra.</summary>
    [Fact]
    public void Drum_DryWithFeedwater_EvaporatesWhatComesIn()
    {
        var drum = new Drum("B-1", areaM2: 1.0, heightM: 1.0, waterDensityKgPerM3: 900.0, evaporationEnthalpyJPerKg: 2.0e6);
        drum.HeatW = 4.0e6;
        drum.FeedwaterFlowKgPerS = 0.5;

        drum.Step(0.0, Dt);

        Assert.Equal(0.0, drum.WaterMassKg);
        Assert.Equal(0.5, drum.SteamGenerationKgPerS, 12);
    }

    [Fact]
    public void Drum_OverflowsAtTop()
    {
        var drum = new Drum("B-1", areaM2: 1.0, heightM: 1.0, waterDensityKgPerM3: 900.0, evaporationEnthalpyJPerKg: 2.0e6, initialLevelM: 0.999);
        drum.FeedwaterFlowKgPerS = 10.0;

        for (int n = 0; n < 1000; n++)
        {
            drum.Step(n * Dt, Dt);
            Assert.True(drum.LevelM <= drum.HeightM);
        }

        Assert.Equal(1.0, drum.LevelM);
        Assert.Equal(10.0, drum.OverflowKgPerS, 9);
    }

    /// <summary>Queimador: em regime, Q = η·PCI·ṁ; 63,2% do degrau em t = τ.</summary>
    [Fact]
    public void Burner_HeatFollowsFuelWithFirstOrderLag()
    {
        var burner = new Burner("BRN-1", lowerHeatingValueJPerKg: 50.0e6, efficiency: 0.85, timeConstantSeconds: 5.0);
        burner.FuelFlowKgPerS = 0.1;
        double target = 0.85 * 50.0e6 * 0.1;

        for (int n = 0; n < 500; n++)
        {
            burner.Step(n * Dt, Dt);
        }

        Assert.True(Math.Abs(burner.HeatW - (target * (1.0 - Math.Exp(-1.0)))) < 0.005 * target);

        for (int n = 500; n < 10_000; n++)
        {
            burner.Step(n * Dt, Dt);
        }

        // Em 100 s = 20·τ ainda falta e⁻²⁰ ≈ 2e-9 do degrau.
        Assert.True(Math.Abs(burner.HeatW - target) < 1e-8 * target, $"calor = {burner.HeatW:R} W");
    }

    /// <summary>
    /// Modelo de pressão v1: com geração e abertura constantes, a pressão vai a P = P_jusante + ṁ/(k·x)
    /// com constante de tempo τ = C/(k·x). Confere contra a solução analítica do degrau.
    /// </summary>
    [Fact]
    public void SteamPressure_StepResponseMatchesAnalyticFirstOrder()
    {
        const double Capacitance = 1.335e-4;
        const double Conductance = 4.451e-6;
        const double Opening = 0.5;
        const double Generation = 2.0;
        var steam = new SteamPressure("B-1", Capacitance, Conductance);
        steam.SteamGenerationKgPerS = Generation;
        steam.OutletOpening = Opening;

        double tau = Capacitance / (Conductance * Opening);
        double final = Atm + (Generation / (Conductance * Opening));
        double worst = 0.0;
        for (int n = 0; n < 60_000; n++)
        {
            steam.Step(n * Dt, Dt);
            double time = (n + 1) * Dt;
            double analytic = final + ((Atm - final) * Math.Exp(-time / tau));
            worst = Math.Max(worst, Math.Abs(steam.PressurePa - analytic) / analytic);
        }

        output.WriteLine($"τ = {tau:F2} s; pressão de regime = {final / 1e5:F4} bar; pressão em 600 s = {steam.PressurePa / 1e5:F4} bar; maior erro relativo = {worst:E3}");
        Assert.True(worst < 1e-9, $"maior erro = {worst:E3}");
        Assert.Equal(Generation, steam.SteamFlowKgPerS, 3); // em regime, sai o que é gerado
    }

    /// <summary>O alívio tira vapor; sem geração a pressão cai até a de jusante e não passa dela.</summary>
    [Fact]
    public void SteamPressure_NeverGoesBelowDownstream()
    {
        var steam = new SteamPressure("B-1", capacitanceKgPerPa: 1.0e-4, outletConductanceKgPerSPa: 4.0e-6, initialPressurePa: 5.0e5);
        steam.OutletOpening = 1.0;
        steam.ReliefFlowKgPerS = 1.0;

        for (int n = 0; n < 60_000; n++)
        {
            steam.Step(n * Dt, Dt);
            Assert.True(steam.PressurePa >= Atm);
        }

        Assert.Equal(Atm, steam.PressurePa);
        Assert.Equal(0.0, steam.SteamFlowKgPerS);
    }

    [Fact]
    public void SteamPressure_ClosedOutlet_IntegratesGeneration()
    {
        var steam = new SteamPressure("B-1", capacitanceKgPerPa: 1.0e-4, outletConductanceKgPerSPa: 4.0e-6);
        steam.SteamGenerationKgPerS = 1.0;
        steam.OutletOpening = 0.0;

        for (int n = 0; n < 1000; n++)
        {
            steam.Step(n * Dt, Dt);
        }

        Assert.Equal(Atm + (10.0 / 1.0e-4), steam.PressurePa, 3); // ΔP = ṁ·t/C
    }

    [Fact]
    public void InvalidParameters_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Drum("B-1", 0.0, 1.0, 900.0, 2.0e6));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Drum("B-1", 1.0, 1.0, 900.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Burner("BRN-1", 50.0e6, efficiency: 1.2, timeConstantSeconds: 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamPressure("B-1", capacitanceKgPerPa: 0.0, outletConductanceKgPerSPa: 1.0e-6));
    }
}
