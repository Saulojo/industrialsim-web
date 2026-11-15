using IndustrialSim.Core.Scenarios;
using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Core.Tests.Scenarios;

/// <summary>
/// Cenário <c>scenarios/caldeira/scenario.json</c> (caldeira v1). Os ensaios leem os valores reais da planta
/// (<c>TB-101.level</c>, <c>TB-101-V.pressure</c>), sem o ruído dos transmissores.
/// </summary>
public class CaldeiraScenarioTests(ITestOutputHelper output)
{
    private const string Level = "TB-101.level";
    private const string Pressure = "TB-101-V.pressure";
    private const int StepsPerSecond = 100;

    private static ScenarioPlant Load() => ScenarioPlant.FromJson(ScenarioFiles.ReadProduct(Path.Combine("caldeira", "scenario.json")));

    /// <summary>O cenário começa em regime: sem mexer em nada, nível e pressão ficam onde estão e nenhuma chave atua.</summary>
    [Fact]
    public void StartsAtSteadyState()
    {
        var plant = Load();
        double level0 = plant.GetSignal(Level);
        double pressure0 = plant.GetSignal(Pressure);

        Run(plant, seconds: 600);

        output.WriteLine($"ordem de execução: {string.Join(" → ", plant.ExecutionOrderIds)}");
        output.WriteLine($"nível: {level0:F5} m → {plant.GetSignal(Level):F5} m; pressão: {pressure0 / 1e5:F4} bar → {plant.GetSignal(Pressure) / 1e5:F4} bar");
        output.WriteLine($"água = {plant.GetSignal("FV-101.flow"):F5} kg/s; vapor gerado = {plant.GetSignal("TB-101.steamGeneration"):F5} kg/s; vapor na saída = {plant.GetSignal("TB-101-V.steamFlow"):F5} kg/s");

        Assert.Equal(0.6, level0, 12);
        Assert.InRange(plant.GetSignal(Level), 0.599, 0.601);
        Assert.InRange(plant.GetSignal(Pressure), 0.995e6, 1.005e6);
        Assert.InRange(plant.GetSignal("TB-101-V.steamFlow"), 1.99, 2.01);
        foreach (string digital in new[] { "LSLL-101", "LSHH-101", "PSH-101", "PSV-101_open" })
        {
            Assert.Equal(0.0, plant.GetOutput(digital));
        }

        Assert.Equal(1.0, plant.GetOutput("P-101_running"));
    }

    /// <summary>Critério 4 da Etapa 3 (1/3): mais água sobe o nível; menos água baixa. A pressão não muda (modelo v1).</summary>
    [Theory]
    [InlineData(+0.05)]
    [InlineData(-0.05)]
    public void FeedwaterStep_MovesLevelInSameDirection(double commandStep)
    {
        var baseline = Load();
        var stepped = Load();
        stepped.SetInput("FV-101_command", stepped.GetInput("FV-101_command") + commandStep);

        var response = RunBoth(baseline, stepped, seconds: 120, Level);

        output.WriteLine($"FV-101 {commandStep:+0.00;-0.00}: nível em 120 s = {response.Final:F5} m (sem degrau: {response.BaselineFinal:F5} m); Δ = {response.Final - response.BaselineFinal:+0.00000;-0.00000} m");
        Assert.Equal(Math.Sign(commandStep), Math.Sign(response.Final - response.BaselineFinal));
        Assert.True(Math.Abs(response.Final - response.BaselineFinal) > 0.003);
        Assert.True(response.AlwaysOnExpectedSide(Math.Sign(commandStep)), "o desvio do nível trocou de sinal durante o ensaio");
        Assert.Equal(baseline.GetSignal(Pressure), stepped.GetSignal(Pressure));
    }

    /// <summary>Critério 4 da Etapa 3 (2/3): mais combustível sobe a pressão; menos combustível baixa.</summary>
    [Theory]
    [InlineData(+0.05)]
    [InlineData(-0.05)]
    public void FuelStep_MovesPressureInSameDirection(double commandStep)
    {
        var baseline = Load();
        var stepped = Load();
        stepped.SetInput("FV-102_command", stepped.GetInput("FV-102_command") + commandStep);

        var response = RunBoth(baseline, stepped, seconds: 300, Pressure);

        output.WriteLine($"FV-102 {commandStep:+0.00;-0.00}: pressão em 300 s = {response.Final / 1e5:F4} bar (sem degrau: {response.BaselineFinal / 1e5:F4} bar); Δ = {(response.Final - response.BaselineFinal) / 1e5:+0.0000;-0.0000} bar");
        Assert.Equal(Math.Sign(commandStep), Math.Sign(response.Final - response.BaselineFinal));
        Assert.True(Math.Abs(response.Final - response.BaselineFinal) > 0.5e5);
        Assert.True(response.AlwaysOnExpectedSide(Math.Sign(commandStep)), "o desvio da pressão trocou de sinal durante o ensaio");

        // Mais fogo evapora mais água: o nível vai no sentido contrário (sem swell no v1).
        Assert.Equal(-Math.Sign(commandStep), Math.Sign(stepped.GetSignal(Level) - baseline.GetSignal(Level)));
    }

    /// <summary>Critério 4 da Etapa 3 (3/3): mais vapor (abrir FV-103) baixa a pressão; menos vapor sobe.</summary>
    [Theory]
    [InlineData(+0.1)]
    [InlineData(-0.1)]
    public void SteamDemandStep_MovesPressureInOppositeDirection(double commandStep)
    {
        var baseline = Load();
        var stepped = Load();
        stepped.SetInput("FV-103_command", stepped.GetInput("FV-103_command") + commandStep);

        var response = RunBoth(baseline, stepped, seconds: 300, Pressure);

        output.WriteLine($"FV-103 {commandStep:+0.00;-0.00}: pressão em 300 s = {response.Final / 1e5:F4} bar (sem degrau: {response.BaselineFinal / 1e5:F4} bar); Δ = {(response.Final - response.BaselineFinal) / 1e5:+0.0000;-0.0000} bar");
        Assert.Equal(-Math.Sign(commandStep), Math.Sign(response.Final - response.BaselineFinal));
        Assert.True(Math.Abs(response.Final - response.BaselineFinal) > 0.5e5);
        Assert.True(response.AlwaysOnExpectedSide(-Math.Sign(commandStep)), "o desvio da pressão trocou de sinal durante o ensaio");
    }

    /// <summary>Os transmissores acompanham o processo: a média da medição fica junto do valor real.</summary>
    [Fact]
    public void Transmitters_FollowProcess()
    {
        var plant = Load();
        plant.SetInput("FV-102_command", 0.6);
        Run(plant, seconds: 300);

        double levelSum = 0.0;
        double pressureSum = 0.0;
        double trueLevelSum = 0.0;
        double truePressureSum = 0.0;
        const int Samples = 1000;
        for (int n = 0; n < Samples; n++)
        {
            plant.Step();
            levelSum += plant.GetOutput("LT-101");
            pressureSum += plant.GetOutput("PT-101");
            trueLevelSum += plant.GetSignal(Level);
            truePressureSum += plant.GetSignal(Pressure);
        }

        Assert.True(Math.Abs((levelSum - trueLevelSum) / Samples) < 0.0005);
        Assert.True(Math.Abs((pressureSum - truePressureSum) / Samples) < 500.0);
        Assert.NotEqual(plant.GetSignal(Level), plant.GetOutput("LT-101")); // há ruído
    }

    /// <summary>
    /// Proteções: com a saída de vapor fechada e fogo alto, a pressão sobe, o pressostato PSH-101 atua antes da
    /// PSV-101, a PSV abre na pressão de ajuste e a pressão não passa dela.
    /// </summary>
    [Fact]
    public void Overpressure_TripsSwitchThenReliefValve()
    {
        var plant = Load();
        plant.SetInput("FV-103_command", 0.0);
        plant.SetInput("FV-102_command", 1.0);

        double? switchTime = null;
        double? reliefTime = null;
        double maxPressure = 0.0;
        int reliefOpenings = 0;
        bool wasOpen = false;
        for (int n = 0; n < 300 * StepsPerSecond; n++)
        {
            plant.Step();
            maxPressure = Math.Max(maxPressure, plant.GetSignal(Pressure));
            bool open = plant.GetOutput("PSV-101_open") >= 0.5;
            switchTime ??= plant.GetOutput("PSH-101") >= 0.5 ? plant.Clock.TimeSeconds : null;
            reliefTime ??= open ? plant.Clock.TimeSeconds : null;
            reliefOpenings += open && !wasOpen ? 1 : 0;
            wasOpen = open;
        }

        output.WriteLine($"PSH-101 atuou em {switchTime:F2} s; PSV-101 abriu em {reliefTime:F2} s e abriu {reliefOpenings} vezes em 300 s; pressão máxima = {maxPressure / 1e5:F4} bar (ajuste 13 bar)");
        Assert.NotNull(switchTime);
        Assert.NotNull(reliefTime);
        Assert.True(switchTime < reliefTime);
        Assert.InRange(maxPressure, 1.3e6, 1.31e6);
        Assert.True(reliefOpenings >= 2, "a PSV deveria abrir e fechar em ciclos");
    }

    /// <summary>Bomba desligada: o nível cai e LSLL-101 atua em 0,30 m; a planta não religa nada sozinha.</summary>
    [Fact]
    public void PumpOff_LevelFallsAndLowLevelSwitchTrips()
    {
        var plant = Load();
        plant.SetInput("P-101_run", 0.0);

        double? tripLevel = null;
        for (int n = 0; n < 900 * StepsPerSecond && tripLevel is null; n++)
        {
            plant.Step();
            if (plant.GetOutput("LSLL-101") >= 0.5)
            {
                tripLevel = plant.GetSignal(Level);
            }
        }

        output.WriteLine($"LSLL-101 atuou em t = {plant.Clock.TimeSeconds:F2} s, com nível = {tripLevel:F5} m");
        Assert.NotNull(tripLevel);
        Assert.InRange(tripLevel.Value, 0.2995, 0.3);
        Assert.Equal(0.0, plant.GetOutput("P-101_running"));
    }

    /// <summary>RNF02 no cenário inteiro: duas execuções iguais dão o mesmo hash; outra semente muda só as medições.</summary>
    [Fact]
    public void SameScript_IsDeterministic()
    {
        var first = RunScript(noiseSeed: null);
        var second = RunScript(noiseSeed: null);
        var otherSeed = RunScript(noiseSeed: 999);

        output.WriteLine($"execução 1: estado = {first.State:x16}; medições = {first.Measurements:x16}");
        output.WriteLine($"execução 2: estado = {second.State:x16}; medições = {second.Measurements:x16}");
        output.WriteLine($"semente 999: medições = {otherSeed.Measurements:x16}");
        Assert.Equal(first, second);
        Assert.NotEqual(first.Measurements, otherSeed.Measurements);
    }

    private static (ulong State, ulong Measurements) RunScript(ulong? noiseSeed)
    {
        var plant = ScenarioPlant.FromJson(ScenarioFiles.ReadProduct(Path.Combine("caldeira", "scenario.json")), noiseSeed: noiseSeed);
        var state = default(StateHasher);
        var measurements = default(StateHasher);
        for (int n = 0; n < 30_000; n++)
        {
            int segment = n / 3000;
            plant.SetInput("FV-101_command", 0.8 + (0.1 * Math.Sin(segment)));
            plant.SetInput("FV-102_command", 0.5 + (0.3 * Math.Cos(segment)));
            plant.SetInput("FV-103_command", 0.5 + (0.2 * Math.Sin(2.0 * segment)));
            plant.Step();

            // Só os estados sem filtro de transmissor entram no hash "físico": compara-se o que não depende da semente.
            state.Add(plant.GetSignal(Level));
            state.Add(plant.GetSignal(Pressure));
            for (int i = 0; i < 4; i++)
            {
                measurements.Add(plant.GetOutput(i));
            }
        }

        return (state.Value, measurements.Value);
    }

    private static void Run(ScenarioPlant plant, int seconds)
    {
        for (int n = 0; n < seconds * StepsPerSecond; n++)
        {
            plant.Step();
        }
    }

    /// <summary>Roda as duas plantas lado a lado e registra o desvio do sinal em relação à planta sem degrau.</summary>
    private static StepResponse RunBoth(ScenarioPlant baseline, ScenarioPlant stepped, int seconds, string signal)
    {
        double minDeviation = double.MaxValue;
        double maxDeviation = double.MinValue;
        for (int n = 0; n < seconds * StepsPerSecond; n++)
        {
            baseline.Step();
            stepped.Step();

            // O primeiro passo ainda não mostra efeito no sinal (atuador e processo em série); a partir de 1 s, sim.
            if (n >= StepsPerSecond)
            {
                double deviation = stepped.GetSignal(signal) - baseline.GetSignal(signal);
                minDeviation = Math.Min(minDeviation, deviation);
                maxDeviation = Math.Max(maxDeviation, deviation);
            }
        }

        return new StepResponse(stepped.GetSignal(signal), baseline.GetSignal(signal), minDeviation, maxDeviation);
    }

    private sealed record StepResponse(double Final, double BaselineFinal, double MinDeviation, double MaxDeviation)
    {
        /// <summary>De 1 s em diante, o desvio teve sempre o sinal esperado (nunca passou para o outro lado).</summary>
        public bool AlwaysOnExpectedSide(int sign) => sign > 0 ? MinDeviation > 0.0 : MaxDeviation < 0.0;
    }
}
