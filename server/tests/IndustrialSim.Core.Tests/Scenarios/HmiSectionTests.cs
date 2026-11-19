using System.Text.Json.Nodes;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.Core.Tests.Scenarios;

/// <summary>
/// Etapa 7: leitura da seção <c>hmi</c> (distúrbios e alarmes da IHM web, D39). Os casos inválidos partem do
/// cenário da caldeira, estragam um item e conferem o caminho do campo e a mensagem.
/// </summary>
public class HmiSectionTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, InvalidCase> Cases = new()
    {
        ["campo desconhecido em hmi"] = new(
            hmi => hmi["trends"] = new JsonArray(),
            "hmi.trends",
            ["campo desconhecido", "disturbances", "alarms"]),

        ["distúrbio em entrada inexistente"] = new(
            hmi => hmi["disturbances"]![0]!["input"] = "FV-999_command",
            "hmi.disturbances[0].input",
            ["entrada externa inexistente: 'FV-999_command'", "FV-103_command"]),

        ["distúrbio em entrada comandada pelo mestre"] = new(
            hmi => hmi["disturbances"]![0]!["input"] = "FV-101_command",
            "hmi.disturbances[0].input",
            ["'FV-101_command' é comandada pelo mestre", "sobrescrito a cada passo"]),

        ["distúrbio com faixa invertida"] = new(
            hmi => hmi["disturbances"]![0]!["max"] = 0.0,
            "hmi.disturbances[0].max",
            ["o máximo (0) deve ser maior que o mínimo (0)"]),

        ["distúrbio repetido"] = new(
            hmi => hmi["disturbances"]!.AsArray().Add(JsonNode.Parse("""{ "input": "FV-103_command" }""")),
            "hmi.disturbances[1].input",
            ["'FV-103_command' já foi usado em hmi.disturbances[0]"]),

        ["fator de unidade zero"] = new(
            hmi => hmi["disturbances"]![0]!["unitFactor"] = 0.0,
            "hmi.disturbances[0].unitFactor",
            ["não pode ser 0"]),

        ["alarme de saída inexistente"] = new(
            hmi => hmi["alarms"]![0]!["signal"] = "LSLL-999",
            "hmi.alarms[0].signal",
            ["saída externa inexistente: 'LSLL-999'", "LSLL-101"]),

        ["alarme repetido"] = new(
            hmi => hmi["alarms"]![1]!["signal"] = "LSLL-101",
            "hmi.alarms[1].signal",
            ["'LSLL-101' já foi usado em hmi.alarms[0]"]),

        ["alarmes com tipo errado"] = new(
            hmi => hmi["alarms"] = "LSLL-101",
            "hmi.alarms",
            ["esperada uma lista"]),

        ["hmi com tipo errado"] = new(
            hmi => hmi.Parent!["hmi"] = 1,
            "hmi",
            ["esperado um objeto"]),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void InvalidHmiSection_ReportsFieldPathAndClearMessage(string caseName)
    {
        InvalidCase invalid = Cases[caseName];
        JsonNode root = JsonNode.Parse(ScenarioFiles.ReadProduct("caldeira/scenario.json"))!;
        invalid.Break(root["hmi"]!);

        var exception = Assert.Throws<ScenarioValidationException>(() => ScenarioLoader.Load(root.ToJsonString()));

        output.WriteLine($"caso: {caseName}");
        foreach (ScenarioError error in exception.Errors)
        {
            output.WriteLine($"  {error}");
        }

        ScenarioError found = Assert.Single(exception.Errors);
        Assert.Equal(invalid.ExpectedPath, found.Path);
        foreach (string fragment in invalid.ExpectedFragments)
        {
            Assert.Contains(fragment, found.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Um cenário sem a seção <c>hmi</c> continua válido (a seção é opcional).</summary>
    [Fact]
    public void ScenarioWithoutHmi_IsValid()
    {
        JsonNode root = JsonNode.Parse(ScenarioFiles.ReadProduct("caldeira/scenario.json"))!;
        root.AsObject().Remove("hmi");

        Assert.Null(ScenarioLoader.Load(root.ToJsonString()).Hmi);
    }

    /// <summary>Valores padrão: faixa de 0 a 1, sem unidade e fator 1.</summary>
    [Fact]
    public void DisturbanceDefaults_AreZeroToOneWithoutUnit()
    {
        JsonNode root = JsonNode.Parse(ScenarioFiles.ReadProduct("caldeira/scenario.json"))!;
        root["hmi"]!["disturbances"] = JsonNode.Parse("""[{ "input": "FV-103_command" }]""");

        DisturbanceDefinition disturbance = Assert.Single(ScenarioLoader.Load(root.ToJsonString()).Hmi!.Disturbances);

        Assert.Equal(new DisturbanceDefinition("FV-103_command", 0.0, 1.0, string.Empty, 1.0), disturbance);
    }

    /// <summary>A caldeira tem FV-103 como distúrbio e as chaves de nível, pressão e alívio como alarmes.</summary>
    [Fact]
    public void CaldeiraHmiSection_HasSteamValveDisturbanceAndSwitchAlarms()
    {
        HmiDefinition hmi = ScenarioLoader.Load(ScenarioFiles.ReadProduct("caldeira/scenario.json")).Hmi!;

        Assert.Equal([new DisturbanceDefinition("FV-103_command", 0.0, 1.0, "%", 100.0)], hmi.Disturbances);
        Assert.Equal(["LSLL-101", "LSHH-101", "PSH-101", "PSV-101_open"], hmi.Alarms.Select(a => a.Signal));
    }

    private sealed record InvalidCase(Action<JsonNode> Break, string ExpectedPath, string[] ExpectedFragments);
}
