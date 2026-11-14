using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Core.Tests.ReferencePlants;

public class WaterTankPlantTests(ITestOutputHelper output)
{
    private const int Steps = 60_000; // 600 s

    /// <summary>
    /// Critério 6 da Etapa 1 (parte 1): duas execuções com as mesmas entradas e a mesma semente geram o mesmo hash,
    /// tanto do estado físico quanto da medição com ruído, acumulado em todos os passos.
    /// </summary>
    [Fact]
    public void SameInputsAndSeed_ProduceSameHashes()
    {
        var first = RunScript(noiseSeed: 2026);
        var second = RunScript(noiseSeed: 2026);

        output.WriteLine($"semente 2026, execução 1: estado = {first.StateHash:x16}; medição = {first.MeasurementHash:x16}");
        output.WriteLine($"semente 2026, execução 2: estado = {second.StateHash:x16}; medição = {second.MeasurementHash:x16}");

        Assert.Equal(first.StateHash, second.StateHash);
        Assert.Equal(first.MeasurementHash, second.MeasurementHash);
    }

    /// <summary>
    /// Critério 6 da Etapa 1 (parte 2): outra semente muda só o ruído. O hash do estado físico é o mesmo;
    /// o da medição muda; e a diferença entre medição e nível real tem a estatística do ruído pedido.
    /// </summary>
    [Fact]
    public void DifferentSeed_ChangesOnlyNoise()
    {
        var first = RunScript(noiseSeed: 2026);
        var other = RunScript(noiseSeed: 7);

        output.WriteLine($"semente 2026: estado = {first.StateHash:x16}; medição = {first.MeasurementHash:x16}");
        output.WriteLine($"semente 7:    estado = {other.StateHash:x16}; medição = {other.MeasurementHash:x16}");
        output.WriteLine($"ruído (semente 7): média = {other.NoiseMean:E3} m; desvio = {other.NoiseStd:F6} m (pedido 0,002 m)");

        Assert.Equal(first.StateHash, other.StateHash);
        Assert.NotEqual(first.MeasurementHash, other.MeasurementHash);
        Assert.Equal(first.FinalLevelM, other.FinalLevelM);
        Assert.InRange(other.NoiseStd, 0.0019, 0.0021);
        Assert.InRange(other.NoiseMean, -0.0001, 0.0001);
    }

    /// <summary>Entradas diferentes mudam o estado: o hash não é insensível.</summary>
    [Fact]
    public void DifferentInputs_ChangeStateHash()
    {
        var first = RunScript(noiseSeed: 2026);
        var other = RunScript(noiseSeed: 2026, valveBias: 1e-12);

        Assert.NotEqual(first.StateHash, other.StateHash);
    }

    /// <summary>Sem ruído, a medição é o próprio nível.</summary>
    [Fact]
    public void ZeroNoise_MeasurementEqualsLevel()
    {
        var plant = new WaterTankPlant(noiseSeed: 1, levelNoiseStdM: 0.0) { PumpRun = true, InletValveCommand = 0.5 };

        for (int n = 0; n < 1000; n++)
        {
            plant.Step();
            Assert.Equal(plant.LevelM, plant.MeasuredLevelM);
        }
    }

    /// <summary>A planta não age sozinha: sem comando, a bomba fica parada e o tanque só esvazia.</summary>
    [Fact]
    public void WithoutCommands_PlantOnlyDrains()
    {
        var plant = new WaterTankPlant(noiseSeed: 1);

        double previous = plant.LevelM;
        for (int n = 0; n < 2000; n++)
        {
            plant.Step();
            Assert.True(plant.LevelM < previous);
            previous = plant.LevelM;
        }

        Assert.Equal(0.0, plant.Pump.FlowM3PerS);
        Assert.Equal(0.0, plant.InletValve.Position);
    }

    /// <summary>
    /// Malha fechada com o PID de referência (que fica fora da planta): o nível vai ao setpoint,
    /// e volta a ele depois de um distúrbio de carga.
    /// </summary>
    [Fact]
    public void ReferencePid_HoldsLevelAtSetpoint()
    {
        const double Setpoint = 1.0;
        var plant = new WaterTankPlant(noiseSeed: 3, levelNoiseStdM: 0.001) { PumpRun = true };
        var pid = new PidController(kp: 5.0, ki: 0.25, kd: 0.0, outputMin: 0.0, outputMax: 1.0);

        RunClosedLoop(plant, pid, Setpoint, seconds: 600.0);
        double levelBeforeDisturbance = plant.LevelM;

        plant.OutletOpening = 0.6;
        RunClosedLoop(plant, pid, Setpoint, seconds: 600.0);

        output.WriteLine($"setpoint = {Setpoint} m; nível em 600 s = {levelBeforeDisturbance:F5} m; nível em 1200 s (após distúrbio) = {plant.LevelM:F5} m");
        Assert.InRange(levelBeforeDisturbance, Setpoint - 0.005, Setpoint + 0.005);
        Assert.InRange(plant.LevelM, Setpoint - 0.005, Setpoint + 0.005);
    }

    private static void RunClosedLoop(WaterTankPlant plant, PidController pid, double setpoint, double seconds)
    {
        int steps = (int)Math.Round(seconds / plant.Clock.StepSeconds);
        for (int n = 0; n < steps; n++)
        {
            plant.InletValveCommand = pid.Update(setpoint, plant.MeasuredLevelM, plant.Clock.StepSeconds);
            plant.Step();
        }
    }

    /// <summary>
    /// Roteiro fixo de 600 s em malha aberta: a bomba liga e desliga, a válvula e a carga variam.
    /// Os comandos dependem só do número do passo, nunca da medição, para o ruído não voltar à física.
    /// </summary>
    private static RunResult RunScript(ulong noiseSeed, double valveBias = 0.0)
    {
        var plant = new WaterTankPlant(noiseSeed);
        var stateHasher = default(StateHasher);
        var measurementHasher = default(StateHasher);
        double noiseSum = 0.0;
        double noiseSumSquares = 0.0;

        for (int n = 0; n < Steps; n++)
        {
            int segment = n / 2000; // comandos mudam a cada 20 s
            plant.PumpRun = segment % 5 != 4;
            plant.InletValveCommand = 0.5 + (0.45 * Math.Sin(segment)) + valveBias;
            plant.OutletOpening = 0.7 + (0.3 * Math.Cos(3.0 * segment));

            plant.Step();

            plant.AddStateTo(ref stateHasher);
            measurementHasher.Add(plant.MeasuredLevelM);
            double noise = plant.MeasuredLevelM - plant.LevelM;
            noiseSum += noise;
            noiseSumSquares += noise * noise;
        }

        double mean = noiseSum / Steps;
        double std = Math.Sqrt((noiseSumSquares / Steps) - (mean * mean));
        return new RunResult(stateHasher.Value, measurementHasher.Value, plant.LevelM, mean, std);
    }

    private sealed record RunResult(ulong StateHash, ulong MeasurementHash, double FinalLevelM, double NoiseMean, double NoiseStd);
}
