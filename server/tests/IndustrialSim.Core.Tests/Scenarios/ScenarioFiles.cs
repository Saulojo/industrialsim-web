namespace IndustrialSim.Core.Tests.Scenarios;

/// <summary>Lê os cenários copiados para a pasta de saída dos testes.</summary>
internal static class ScenarioFiles
{
    /// <summary>Cenário de teste, em <c>Scenarios/Files</c>.</summary>
    public static string ReadTest(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios", "Files", fileName));

    /// <summary>Arquivo da pasta <c>scenarios/</c> do repositório (ex.: <c>caldeira/scenario.json</c>).</summary>
    public static string ReadProduct(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProductScenarios", relativePath));
}
