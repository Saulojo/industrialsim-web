using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;

namespace IndustrialSim.Modbus.Tests;

/// <summary>O cenário da caldeira (o do produto, copiado para a pasta de saída) e os endereços do seu mapa.</summary>
internal static class Caldeira
{
    // Coil
    public const int PumpRun = 0;

    // Entradas discretas
    public const int PumpRunning = 0;
    public const int WatchdogStatus = 5;

    // Holding registers
    public const int WaterValveCommand = 0;
    public const int FuelValveCommand = 1;
    public const int Heartbeat = 10;

    // Input registers
    public const int Level = 0;
    public const int WaterValvePosition = 4;
    public const int Diagnostics = 100;

    public static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProductScenarios", "caldeira", "scenario.json"));

    public static PlantIo Create() => new(ScenarioPlant.FromJson(Json));
}
