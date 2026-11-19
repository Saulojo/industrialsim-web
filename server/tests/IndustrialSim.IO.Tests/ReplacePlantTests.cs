using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.IO.Tests;

/// <summary>Etapa 7: reinício da planta com a imagem de I/O mantida (D41).</summary>
public class ReplacePlantTests(ITestOutputHelper output)
{
    /// <summary>
    /// Depois do reinício, a planta segue do estado inicial com os comandos que estão na imagem: fica igual,
    /// bit a bit, a uma planta nova que recebeu os mesmos comandos desde o primeiro passo.
    /// </summary>
    [Fact]
    public void AfterReplace_PlantMatchesFreshPlantWithSameCommands()
    {
        PlantIo io = Caldeira.Create();
        io.WriteCounts(Caldeira.WaterValveCommand, 6912); // 25%: o nível cai
        for (int i = 0; i < 3000; i++)
        {
            io.Step();
        }

        short levelBefore = io.Image.Snapshot().Counts(Caldeira.Level);
        io.ReplacePlant(ScenarioPlant.Create(io.Plant.Definition));

        // As medições já mostram o estado inicial, antes de qualquer passo.
        Assert.Equal(13824, io.Image.Snapshot().Counts(Caldeira.Level));
        Assert.Equal(0, io.Plant.Clock.StepCount);

        PlantIo twin = Caldeira.Create();
        twin.WriteCounts(Caldeira.WaterValveCommand, 6912);
        for (int i = 0; i < 1000; i++)
        {
            io.Step();
            twin.Step();
        }

        output.WriteLine($"LT-101 antes do reinício = {levelBefore}; hash = {io.Plant.StateHash():X16}, gêmea = {twin.Plant.StateHash():X16}");
        Assert.True(levelBefore < 13824);
        Assert.Equal(twin.Plant.StateHash(), io.Plant.StateHash());
        Assert.Equal(twin.Image.Snapshot().InputRegisters, io.Image.Snapshot().InputRegisters);
    }

    /// <summary>O watchdog disparado volta a contar do zero no reinício.</summary>
    [Fact]
    public void Replace_ClearsTrippedWatchdog()
    {
        PlantIo io = Caldeira.Create(new IoSettings(WatchdogEnabled: true, WatchdogTimeoutSeconds: 0.5));
        for (int i = 0; i < 100; i++)
        {
            io.Step();
        }

        Assert.True(io.WatchdogTripped);

        io.ReplacePlant(ScenarioPlant.Create(io.Plant.Definition));

        Assert.False(io.WatchdogTripped);
        Assert.False(io.Image.Snapshot().DiscreteInputs[Caldeira.WatchdogStatus]);
    }

    [Fact]
    public void Replace_WithPlantFromOtherDefinition_Throws()
    {
        PlantIo io = Caldeira.Create();

        Assert.Throws<ArgumentException>(() => io.ReplacePlant(ScenarioPlant.FromJson(Caldeira.Json)));
    }
}
