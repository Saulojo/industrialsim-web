using IndustrialSim.Core.Components;
using IndustrialSim.Core.Numerics;

namespace IndustrialSim.Core.Tests.Components;

public class TankTests(ITestOutputHelper output)
{
    private const double Dt = 0.01;
    private const double Gravity = PhysicalConstants.StandardGravityMPerS2;

    /// <summary>
    /// Critério 3 da Etapa 1: a variação de volume iguala ∫(Qin − Qout)dt, com erro relativo &lt; 1e-6 em 600 s.
    /// A integral é calculada aqui, fora do tanque: Qin é constante em cada passo (soma exata) e Qout é
    /// integrada pela regra do trapézio, com a vazão lida no início e no fim de cada passo.
    /// </summary>
    [Fact]
    public void VolumeChange_EqualsIntegralOfNetFlow()
    {
        var tank = new Tank("TQ-1", areaM2: 2.0, heightM: 3.0, outletAreaM2: 0.005, initialLevelM: 1.5);
        double initialVolume = tank.VolumeM3;
        double inflowIntegral = 0.0;
        double outflowIntegral = 0.0;
        double minLevel = tank.LevelM;
        double maxLevel = tank.LevelM;

        const int Steps = 60_000; // 600 s
        for (int n = 0; n < Steps; n++)
        {
            int segment = n / 3000; // entradas mudam a cada 30 s
            tank.InflowM3PerS = 0.012 + (0.008 * Math.Sin(segment));
            tank.OutletOpening = 0.6 + (0.4 * Math.Cos(2.0 * segment));

            tank.UpdateOutputs();
            double outflowAtStart = tank.OutflowM3PerS;
            tank.Step(n * Dt, Dt);
            double outflowAtEnd = tank.OutflowM3PerS;

            inflowIntegral += tank.InflowM3PerS * Dt;
            outflowIntegral += 0.5 * (outflowAtStart + outflowAtEnd) * Dt;
            minLevel = Math.Min(minLevel, tank.LevelM);
            maxLevel = Math.Max(maxLevel, tank.LevelM);

            Assert.Equal(0.0, tank.OverflowM3PerS);
        }

        double volumeChange = tank.VolumeM3 - initialVolume;
        double netFlowIntegral = inflowIntegral - outflowIntegral;
        double relativeError = Math.Abs(volumeChange - netFlowIntegral) / Math.Abs(volumeChange);
        output.WriteLine($"ΔV = {volumeChange:R} m³; ∫(Qin − Qout)dt = {netFlowIntegral:R} m³; erro relativo = {relativeError:E3}");
        output.WriteLine($"∫Qin dt = {inflowIntegral:R} m³; ∫Qout dt = {outflowIntegral:R} m³; nível entre {minLevel:F4} m e {maxLevel:F4} m");

        // O ensaio só vale se o tanque não saturou e se ΔV não é pequeno a ponto de inflar o erro relativo.
        Assert.True(minLevel > 0.1 && maxLevel < 2.9, $"nível entre {minLevel} e {maxLevel}");
        Assert.True(Math.Abs(volumeChange) > 0.1, $"ΔV = {volumeChange}");
        Assert.True(relativeError < 1e-6, $"erro relativo = {relativeError:E3}");
    }

    /// <summary>Com o tanque transbordando, o balanço fecha quando o transbordo entra na conta.</summary>
    [Fact]
    public void VolumeChange_EqualsIntegralOfNetFlow_WhenOverflowing()
    {
        var tank = new Tank("TQ-1", areaM2: 1.0, heightM: 1.0, outletAreaM2: 0.002, initialLevelM: 0.5);
        tank.InflowM3PerS = 0.05;
        tank.OutletOpening = 1.0;
        double initialVolume = tank.VolumeM3;
        double netIntegral = 0.0;
        double overflowIntegral = 0.0;

        for (int n = 0; n < 6000; n++)
        {
            tank.UpdateOutputs();
            double outflowAtStart = tank.OutflowM3PerS;
            tank.Step(n * Dt, Dt);

            netIntegral += (tank.InflowM3PerS - (0.5 * (outflowAtStart + tank.OutflowM3PerS))) * Dt;
            overflowIntegral += tank.OverflowM3PerS * Dt;
        }

        double volumeChange = tank.VolumeM3 - initialVolume;
        double relativeError = Math.Abs(volumeChange - (netIntegral - overflowIntegral)) / volumeChange;
        output.WriteLine($"ΔV = {volumeChange:R} m³; transbordo = {overflowIntegral:R} m³; erro relativo = {relativeError:E3}");

        Assert.Equal(1.0, tank.LevelM);
        Assert.True(overflowIntegral > 1.0);
        Assert.True(relativeError < 1e-6, $"erro relativo = {relativeError:E3}");
    }

    /// <summary>
    /// Critério 4 da Etapa 1: bomba desligada e saída aberta, o nível cai.
    /// A bomba nunca é ligada; o nível tem de cair a cada passo.
    /// </summary>
    [Fact]
    public void PumpOffAndOutletOpen_LevelFalls()
    {
        var pump = new Pump("P-101", ratedFlowM3PerS: 0.02);
        var tank = new Tank("TQ-1", areaM2: 1.0, heightM: 2.0, outletAreaM2: 0.01, initialLevelM: 1.0);
        tank.OutletOpening = 1.0;

        double previous = tank.LevelM;
        for (int n = 0; n < 3000; n++)
        {
            pump.Step(n * Dt, Dt);
            tank.InflowM3PerS = pump.FlowM3PerS;
            tank.Step(n * Dt, Dt);

            Assert.True(tank.LevelM < previous, $"passo {n}: nível {tank.LevelM} não caiu (anterior {previous})");
            previous = tank.LevelM;
        }

        output.WriteLine($"nível: 1,0000 m -> {tank.LevelM:F4} m em 30 s, com a bomba desligada");
        Assert.Equal(0.0, pump.FlowM3PerS);
        Assert.True(tank.LevelM < 0.5);
    }

    /// <summary>Ligar a bomba enche o tanque; desligar faz o nível voltar a cair (bomba comandável).</summary>
    [Fact]
    public void PumpOnThenOff_LevelRisesThenFalls()
    {
        var pump = new Pump("P-101", ratedFlowM3PerS: 0.05);
        var tank = new Tank("TQ-1", areaM2: 1.0, heightM: 2.0, outletAreaM2: 0.005, initialLevelM: 0.5);
        tank.OutletOpening = 1.0;

        pump.Run = true;
        RunPumpAndTank(pump, tank, seconds: 20.0);
        double levelAfterFilling = tank.LevelM;

        pump.Run = false;
        RunPumpAndTank(pump, tank, seconds: 20.0);

        Assert.True(levelAfterFilling > 0.5);
        Assert.True(tank.LevelM < levelAfterFilling);
    }

    /// <summary>
    /// Esvaziamento por Torricelli tem solução fechada: √h(t) = √h₀ − Cd·a·√(2g)·t/(2A).
    /// </summary>
    [Fact]
    public void Draining_MatchesAnalyticSolution()
    {
        const double Area = 1.5;
        const double OutletArea = 0.004;
        const double Cd = 0.61;
        const double InitialLevel = 1.2;
        var tank = new Tank("TQ-1", Area, heightM: 2.0, OutletArea, Cd, InitialLevel);
        tank.OutletOpening = 1.0;

        for (int n = 0; n < 6000; n++)
        {
            tank.Step(n * Dt, Dt);
        }

        double root = Math.Sqrt(InitialLevel) - (Cd * OutletArea * Math.Sqrt(2.0 * Gravity) * 60.0 / (2.0 * Area));
        double expected = root * root;
        double error = Math.Abs(tank.LevelM - expected);
        output.WriteLine($"nível em 60 s = {tank.LevelM:R} m; analítico = {expected:R} m; erro = {error:E3}");

        Assert.True(error < 1e-10, $"erro = {error:E3}");
    }

    /// <summary>Em regime, Qin = Qout: h = (Qin / (Cd·a·x))² / (2g).</summary>
    [Fact]
    public void SteadyState_MatchesTorricelli()
    {
        var tank = new Tank("TQ-1", areaM2: 1.0, heightM: 2.0, outletAreaM2: 0.01, dischargeCoefficient: 0.61);
        tank.InflowM3PerS = 0.01;
        tank.OutletOpening = 0.8;

        for (int n = 0; n < 120_000; n++)
        {
            tank.Step(n * Dt, Dt);
        }

        double expected = Math.Pow(0.01 / (0.61 * 0.01 * 0.8), 2.0) / (2.0 * Gravity);
        Assert.True(Math.Abs(tank.LevelM - expected) < 1e-9, $"nível = {tank.LevelM:R}; esperado = {expected:R}");
        Assert.True(Math.Abs(tank.OutflowM3PerS - 0.01) < 1e-9);
    }

    /// <summary>Critério 5 da Etapa 1 (parte 1): esvaziando, o nível chega a zero e nunca fica negativo.</summary>
    [Fact]
    public void Draining_NeverGoesBelowZero()
    {
        var tank = new Tank("TQ-1", areaM2: 0.5, heightM: 1.0, outletAreaM2: 0.02, initialLevelM: 1.0);
        tank.OutletOpening = 1.0;

        for (int n = 0; n < 20_000; n++)
        {
            tank.Step(n * Dt, Dt);
            Assert.True(tank.LevelM >= 0.0 && double.IsFinite(tank.LevelM), $"passo {n}: nível = {tank.LevelM}");
            Assert.True(tank.OutflowM3PerS >= 0.0);
        }

        // Tempo analítico de esvaziamento: 2A·√h₀ / (Cd·a·√(2g)) ≈ 18,5 s; em 200 s o tanque está vazio.
        Assert.True(tank.LevelM < 1e-12, $"nível final = {tank.LevelM:E3}");
    }

    /// <summary>Critério 5 da Etapa 1 (parte 2): enchendo, o nível para na altura do tanque e o excesso transborda.</summary>
    [Fact]
    public void Filling_NeverExceedsHeight()
    {
        var tank = new Tank("TQ-1", areaM2: 0.5, heightM: 1.0, outletAreaM2: 0.001, initialLevelM: 0.9);
        tank.InflowM3PerS = 0.1;
        tank.OutletOpening = 1.0;

        for (int n = 0; n < 6000; n++)
        {
            tank.Step(n * Dt, Dt);
            Assert.True(tank.LevelM <= tank.HeightM, $"passo {n}: nível = {tank.LevelM}");
        }

        Assert.Equal(tank.HeightM, tank.LevelM);
        Assert.True(Math.Abs(tank.OverflowM3PerS - (0.1 - tank.OutflowM3PerS)) < 1e-12);
    }

    /// <summary>
    /// Critério 5 da Etapa 1 (parte 3): com entradas aleatórias (semente fixa), inclusive fora da faixa,
    /// o nível fica em [0, altura] em todos os passos e o ensaio toca os dois limites.
    /// </summary>
    [Fact]
    public void RandomInputs_LevelStaysWithinTank()
    {
        var random = new SeededRandom(101);
        var tank = new Tank("TQ-1", areaM2: 0.2, heightM: 1.0, outletAreaM2: 0.01, initialLevelM: 0.5);
        int stepsEmpty = 0;
        int stepsFull = 0;

        for (int n = 0; n < 500_000; n++)
        {
            if (n % 500 == 0)
            {
                tank.InflowM3PerS = -0.02 + (0.10 * random.NextDouble());
                tank.OutletOpening = -0.5 + (2.0 * random.NextDouble());
            }

            tank.Step(n * Dt, Dt);

            Assert.True(tank.LevelM >= 0.0 && tank.LevelM <= tank.HeightM, $"passo {n}: nível = {tank.LevelM}");
            Assert.True(tank.OutflowM3PerS >= 0.0 && tank.OverflowM3PerS >= 0.0);
            stepsEmpty += tank.LevelM < 1e-9 ? 1 : 0;
            stepsFull += tank.LevelM == tank.HeightM ? 1 : 0;
        }

        output.WriteLine($"500000 passos: {stepsEmpty} com o tanque vazio, {stepsFull} com o tanque cheio");
        Assert.True(stepsEmpty > 0, "o ensaio deveria esvaziar o tanque alguma vez");
        Assert.True(stepsFull > 0, "o ensaio deveria encher o tanque alguma vez");
    }

    [Fact]
    public void NegativeInflow_IsTreatedAsZero()
    {
        var tank = new Tank("TQ-1", areaM2: 1.0, heightM: 1.0, outletAreaM2: 0.01, initialLevelM: 0.5);
        tank.InflowM3PerS = -1.0;
        tank.OutletOpening = 0.0;

        tank.Step(0.0, Dt);

        Assert.Equal(0.5, tank.LevelM);
    }

    [Fact]
    public void InitialLevel_IsLimitedToTank()
    {
        Assert.Equal(2.0, new Tank("TQ-1", 1.0, heightM: 2.0, 0.01, initialLevelM: 5.0).LevelM);
        Assert.Equal(0.0, new Tank("TQ-1", 1.0, heightM: 2.0, 0.01, initialLevelM: -1.0).LevelM);
    }

    [Fact]
    public void InvalidParameters_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tank("TQ-1", areaM2: 0.0, heightM: 1.0, outletAreaM2: 0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tank("TQ-1", areaM2: 1.0, heightM: -1.0, outletAreaM2: 0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tank("TQ-1", areaM2: 1.0, heightM: 1.0, outletAreaM2: -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tank("TQ-1", areaM2: 1.0, heightM: 1.0, outletAreaM2: 0.01, dischargeCoefficient: 0.0));
    }

    private static void RunPumpAndTank(Pump pump, Tank tank, double seconds)
    {
        int steps = (int)Math.Round(seconds / Dt);
        for (int n = 0; n < steps; n++)
        {
            pump.Step(n * Dt, Dt);
            tank.InflowM3PerS = pump.FlowM3PerS;
            tank.Step(n * Dt, Dt);
        }
    }
}
