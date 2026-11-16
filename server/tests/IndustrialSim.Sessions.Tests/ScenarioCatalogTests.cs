using System.Text.Json.Nodes;

namespace IndustrialSim.Sessions.Tests;

public sealed class ScenarioCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "industrialsim-catalog-" + Guid.NewGuid().ToString("N"));

    public ScenarioCatalogTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ProductScenarios_IncludeTheBoiler()
    {
        ScenarioCatalog catalog = ProductScenarios.Catalog();

        Assert.True(catalog.TryGet("caldeira", out ScenarioEntry? caldeira));
        Assert.NotNull(caldeira.Definition.Io);
        Assert.Contains(catalog.Scenarios, s => s.Id == "caldeira");
    }

    [Fact]
    public void InvalidOrUnsuitableFiles_AreSkipped_AndTheValidOnesLoad()
    {
        string caldeira = ProductScenarios.CaldeiraJson;
        Write("a-valido", caldeira);
        Write("b-json-quebrado", "{ \"schemaVersion\": 1, ");
        Write("c-sem-io", Modify(caldeira, root => { root["id"] = "sem-io"; root.Remove("io"); }));
        Write("d-outro-passo", Modify(caldeira, root => { root["id"] = "outro-passo"; root["stepSeconds"] = 0.02; }));
        Write("e-repetido", caldeira);
        Write("f-componente-desconhecido", Modify(caldeira, root =>
        {
            root["id"] = "componente-desconhecido";
            root["components"]![0]!["type"] = "nao-existe";
        }));
        Directory.CreateDirectory(Path.Combine(_directory, "g-sem-arquivo"));

        ScenarioCatalog catalog = ScenarioCatalog.Load(_directory, 0.01);

        ScenarioEntry only = Assert.Single(catalog.Scenarios);
        Assert.Equal("caldeira", only.Id);
        Assert.EndsWith(Path.Combine("a-valido", "scenario.json"), only.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDirectory_GivesAnEmptyCatalog()
    {
        ScenarioCatalog catalog = ScenarioCatalog.Load(Path.Combine(_directory, "nao-existe"), 0.01);

        Assert.Empty(catalog.Scenarios);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string Modify(string json, Action<JsonObject> change)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        change(root);
        return root.ToJsonString();
    }

    private void Write(string folder, string json)
    {
        string path = Path.Combine(_directory, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ScenarioCatalog.ScenarioFileName), json);
    }
}
