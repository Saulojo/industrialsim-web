using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.IO.Tests;

/// <summary>O cenário da caldeira (o do produto, copiado para a pasta de saída) e os endereços do seu mapa.</summary>
internal static class Caldeira
{
    // Coil
    public const int PumpRun = 0;

    // Entradas discretas
    public const int PumpRunning = 0;
    public const int LevelLowLow = 1;
    public const int WatchdogStatus = 5;

    // Holding registers
    public const int WaterValveCommand = 0;
    public const int FuelValveCommand = 1;
    public const int Heartbeat = 10;

    // Input registers
    public const int Level = 0;
    public const int Pressure = 1;
    public const int WaterValvePosition = 4;
    public const int Diagnostics = 100;

    public static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProductScenarios", "caldeira", "scenario.json"));

    public static PlantIo Create(IoSettings? settings = null) => new(ScenarioPlant.FromJson(Json), settings);

    /// <summary>Contagens (lidas como <c>INT</c>) de um input register.</summary>
    public static short Counts(this IoSnapshot snapshot, int register) => AnalogCodec.FromRegister(snapshot.InputRegisters[register]);

    /// <summary><c>float32</c> que começa num input register, na ordem de palavras do snapshot.</summary>
    public static float Float(this IoSnapshot snapshot, int register) =>
        AnalogCodec.ReadFloat32([snapshot.InputRegisters[register], snapshot.InputRegisters[register + 1]], snapshot.WordOrder);

    public static void WriteCounts(this PlantIo io, int register, int counts) =>
        Assert.True(io.Image.TryWriteHoldingRegister(register, AnalogCodec.ToRegister((short)counts)));
}
