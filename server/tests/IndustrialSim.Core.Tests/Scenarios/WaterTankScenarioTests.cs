using IndustrialSim.Core.Scenarios;
using IndustrialSim.Core.Simulation;
using IndustrialSim.Core.Tests.ReferencePlants;

namespace IndustrialSim.Core.Tests.Scenarios;

public class WaterTankScenarioTests(ITestOutputHelper output)
{
    private const int Steps = 60_000; // 600 s

    /// <summary>
    /// Critério 1 da Etapa 3: o tanque descrito em JSON gera resultado idêntico, bit a bit, ao modelo codificado
    /// da Etapa 1 (<see cref="WaterTankPlant"/>) para o mesmo roteiro. A comparação é feita a cada passo, nos
    /// bits do nível real, do nível medido (com ruído) e do hash do estado, e no hash acumulado dos 600 s.
    /// </summary>
    [Fact]
    public void JsonTank_IsBitIdenticalToCodedPlant()
    {
        var coded = new WaterTankPlant(noiseSeed: 2026);
        var fromJson = ScenarioPlant.FromJson(ScenarioFiles.ReadTest("water-tank.json"));
        int pumpRun = fromJson.InputIndex("pumpRun");
        int valveCommand = fromJson.InputIndex("valveCommand");
        int outletOpening = fromJson.InputIndex("outletOpening");
        int level = fromJson.OutputIndex("level");
        int measuredLevel = fromJson.OutputIndex("measuredLevel");

        var codedStates = default(StateHasher);
        var jsonStates = default(StateHasher);
        var codedMeasurements = default(StateHasher);
        var jsonMeasurements = default(StateHasher);

        for (int n = 0; n < Steps; n++)
        {
            // O mesmo roteiro do teste de determinismo da Etapa 1: comandos mudam a cada 20 s.
            int segment = n / 2000;
            bool run = segment % 5 != 4;
            double command = 0.5 + (0.45 * Math.Sin(segment));
            double opening = 0.7 + (0.3 * Math.Cos(3.0 * segment));

            coded.PumpRun = run;
            coded.InletValveCommand = command;
            coded.OutletOpening = opening;
            fromJson.SetInput(pumpRun, run ? 1.0 : 0.0);
            fromJson.SetInput(valveCommand, command);
            fromJson.SetInput(outletOpening, opening);

            coded.Step();
            fromJson.Step();

            Assert.True(
                BitConverter.DoubleToInt64Bits(coded.LevelM) == BitConverter.DoubleToInt64Bits(fromJson.GetOutput(level)),
                $"passo {n}: nível {coded.LevelM:R} (código) ≠ {fromJson.GetOutput(level):R} (JSON)");
            Assert.True(
                BitConverter.DoubleToInt64Bits(coded.MeasuredLevelM) == BitConverter.DoubleToInt64Bits(fromJson.GetOutput(measuredLevel)),
                $"passo {n}: medição {coded.MeasuredLevelM:R} (código) ≠ {fromJson.GetOutput(measuredLevel):R} (JSON)");

            coded.AddStateTo(ref codedStates);
            fromJson.AddStateTo(ref jsonStates);
            Assert.True(codedStates.Value == jsonStates.Value, $"passo {n}: hash de estado diferente");

            codedMeasurements.Add(coded.MeasuredLevelM);
            jsonMeasurements.Add(fromJson.GetOutput(measuredLevel));
        }

        output.WriteLine($"código (Etapa 1): estado = {codedStates.ToHex()}; medição = {codedMeasurements.ToHex()}; nível final = {coded.LevelM:R} m");
        output.WriteLine($"JSON   (Etapa 3): estado = {jsonStates.ToHex()}; medição = {jsonMeasurements.ToHex()}; nível final = {fromJson.GetOutput(level):R} m");
        output.WriteLine($"ordem de execução do JSON: {string.Join(" → ", fromJson.ExecutionOrderIds)}");

        Assert.Equal(codedStates.Value, jsonStates.Value);
        Assert.Equal(codedMeasurements.Value, jsonMeasurements.Value);
        Assert.Equal(Steps, fromJson.Clock.StepCount);
    }

    /// <summary>A ordem dos componentes no arquivo não muda o resultado: quem manda é a ordem das ligações.</summary>
    [Fact]
    public void ComponentOrderInFile_DoesNotChangeResult()
    {
        string json = ScenarioFiles.ReadTest("water-tank.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var components = node["components"]!.AsArray();
        var reversed = components.Select(c => c!.DeepClone()).Reverse().ToArray();
        components.Clear();
        foreach (var component in reversed)
        {
            components.Add(component);
        }

        var original = ScenarioPlant.FromJson(json);
        var shuffled = ScenarioPlant.FromJson(node.ToJsonString());
        Assert.Equal(["P-101", "FV-101", "TQ-101", "LT-101"], shuffled.ExecutionOrderIds);

        foreach (var plant in new[] { original, shuffled })
        {
            plant.SetInput("pumpRun", 1.0);
            plant.SetInput("valveCommand", 0.7);
            for (int n = 0; n < 5000; n++)
            {
                plant.Step();
            }
        }

        Assert.Equal(original.GetOutput("level"), shuffled.GetOutput("level"));
        Assert.Equal(original.GetOutput("measuredLevel"), shuffled.GetOutput("measuredLevel"));
    }

    /// <summary>Outra semente muda só o ruído: o estado físico é o mesmo, a medição muda (RNF02).</summary>
    [Fact]
    public void DifferentSeed_ChangesOnlyNoise()
    {
        string json = ScenarioFiles.ReadTest("water-tank.json");
        var first = ScenarioPlant.FromJson(json);
        var other = ScenarioPlant.FromJson(json, noiseSeed: 7);

        foreach (var plant in new[] { first, other })
        {
            plant.SetInput("pumpRun", 1.0);
            plant.SetInput("valveCommand", 0.6);
            for (int n = 0; n < 2000; n++)
            {
                plant.Step();
            }
        }

        Assert.Equal(first.StateHash(), other.StateHash());
        Assert.Equal(first.GetOutput("level"), other.GetOutput("level"));
        Assert.NotEqual(first.GetOutput("measuredLevel"), other.GetOutput("measuredLevel"));
    }

    /// <summary>Um comando escrito só vale a partir do passo seguinte, e a planta não age sozinha.</summary>
    [Fact]
    public void WithoutCommands_PlantOnlyDrains()
    {
        var plant = ScenarioPlant.FromJson(ScenarioFiles.ReadTest("water-tank.json"));

        Assert.Equal(0.5, plant.GetOutput("level"));
        Assert.Equal(0.5, plant.GetOutput("measuredLevel")); // antes do primeiro passo não há ruído

        double previous = plant.GetOutput("level");
        for (int n = 0; n < 2000; n++)
        {
            plant.Step();
            Assert.True(plant.GetOutput("level") < previous);
            previous = plant.GetOutput("level");
        }

        Assert.Equal(0.0, plant.GetSignal("P-101.flow"));

        plant.SetInput("pumpRun", 1.0);
        Assert.Equal(0.0, plant.FindComponent("P-101").Inputs["run"]); // ainda não copiado
        plant.Step();
        Assert.Equal(1.0, plant.FindComponent("P-101").Inputs["run"]);
        Assert.True(plant.GetSignal("P-101.flow") > 0.0);
    }
}
