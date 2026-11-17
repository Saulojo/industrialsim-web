using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Sessions.Tests;

/// <summary>Os cenários do produto, copiados para a pasta de saída.</summary>
internal static class ProductScenarios
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "ProductScenarios");

    public static string CaldeiraJson => File.ReadAllText(Path.Combine(Directory, "caldeira", ScenarioCatalog.ScenarioFileName));

    public static ScenarioCatalog Catalog() => ScenarioCatalog.Load(Directory, new RealTimeLoopOptions().StepSeconds);
}
