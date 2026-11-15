using System.Text.Json.Nodes;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.Core.Tests.Scenarios;

/// <summary>
/// Critério 3 da Etapa 3: JSON inválido gera erro claro com o caminho do campo.
/// Cada caso parte do cenário válido <c>water-tank.json</c>, estraga um ponto e confere o caminho e a mensagem.
/// </summary>
public class InvalidScenarioTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, InvalidCase> Cases = new()
    {
        ["tipo de componente desconhecido"] = new(
            root => root["components"]![0]!["type"] = "turbine",
            "components[0].type",
            ["tipo de componente desconhecido: 'turbine'", "pump", "valve", "tank"]),

        ["ligação para sinal inexistente"] = new(
            root => root["connections"]![1]!["to"] = "TQ-101.vazao",
            "connections[1].to",
            ["o componente 'TQ-101' não tem entrada 'vazao'", "inflow", "outletOpening"]),

        ["ligação de componente inexistente"] = new(
            root => root["connections"]![0]!["from"] = "P-999.flow",
            "connections[0].from",
            ["componente inexistente: 'P-999'", "P-101"]),

        ["parâmetro fora da faixa"] = new(
            root => root["components"]![2]!["params"]!["areaM2"] = -1.0,
            "components[2].params.areaM2",
            ["valor -1 fora da faixa", "> 0", "m²"]),

        ["parâmetro obrigatório ausente"] = new(
            root => root["components"]![2]!["params"]!.AsObject().Remove("heightM"),
            "components[2].params.heightM",
            ["campo obrigatório ausente", "altura do tanque"]),

        ["parâmetro com tipo errado"] = new(
            root => root["components"]![0]!["params"]!["ratedFlow"] = "muito",
            "components[0].params.ratedFlow",
            ["esperado um número", "o texto \"muito\""]),

        ["parâmetro desconhecido"] = new(
            root => root["components"]![2]!["params"]!["areaM3"] = 1.0,
            "components[2].params.areaM3",
            ["campo desconhecido para o tipo 'tank'", "areaM2"]),

        ["opção inválida"] = new(
            root => root["components"]![1]!["params"]!["characteristic"] = "parabolic",
            "components[1].params.characteristic",
            ["valor inválido", "linear", "equalPercentage"]),

        ["condição inicial fora da faixa"] = new(
            root => root["components"]![1]!["initial"] = new JsonObject { ["position"] = 1.5 },
            "components[1].initial.position",
            ["valor 1.5 fora da faixa", "≤ 1"]),

        ["identificador repetido"] = new(
            root => root["components"]![1]!["id"] = "P-101",
            "components[1].id",
            ["o identificador 'P-101' já foi usado em components[0]"]),

        ["versão do esquema não suportada"] = new(
            root => root["schemaVersion"] = 99,
            "schemaVersion",
            ["versão 99 não suportada", "versão 1"]),

        ["versão do esquema ausente"] = new(
            root => root.AsObject().Remove("schemaVersion"),
            "schemaVersion",
            ["campo obrigatório ausente"]),

        ["campo desconhecido na raiz"] = new(
            root => root["componentes"] = new JsonArray(),
            "componentes",
            ["campo desconhecido", "components"]),

        ["entrada com duas fontes"] = new(
            root => root["connections"]!.AsArray().Add(new JsonObject { ["from"] = "P-101.flow", ["to"] = "TQ-101.inflow" }),
            "connections[3].to",
            ["a entrada 'TQ-101.inflow' já recebe a saída 'FV-101.flow'", "só pode ter uma fonte"]),

        ["entrada externa sobre entrada já ligada"] = new(
            root => root["inputs"]![0]!["to"]!.AsArray().Add("FV-101.inletFlow"),
            "inputs[0].to[1]",
            ["a entrada 'FV-101.inletFlow' já recebe a saída 'P-101.flow'"]),

        ["saída externa de sinal inexistente"] = new(
            root => root["outputs"]![0]!["from"] = "TQ-101.nivel",
            "outputs[0].from",
            ["o componente 'TQ-101' não tem saída 'nivel'", "level"]),

        ["referência fora do formato"] = new(
            root => root["connections"]![0]!["from"] = "P-101",
            "connections[0].from",
            ["'P-101' não está no formato 'componente.sinal'"]),

        ["passo não positivo"] = new(
            root => root["stepSeconds"] = 0,
            "stepSeconds",
            ["valor 0 fora da faixa", "> 0"]),

        ["nome de saída repetido"] = new(
            root => root["outputs"]![1]!["name"] = "pumpRun",
            "outputs[1].name",
            ["o nome 'pumpRun' já foi usado em inputs[0].name"]),

        ["lista de componentes vazia"] = new(
            root =>
            {
                root["components"] = new JsonArray();
                root["connections"] = new JsonArray();
                root["inputs"] = new JsonArray();
                root["outputs"] = new JsonArray();
            },
            "components",
            ["pelo menos um componente"]),

        ["tipo JSON errado numa lista"] = new(
            root => root["connections"] = "P-101.flow -> FV-101.inletFlow",
            "connections",
            ["esperada uma lista"]),

        ["laço algébrico"] = new(
            root =>
            {
                // Dois transmissores sem amortecimento (sem estado), um medindo o outro.
                root["components"]!.AsArray().Add(new JsonObject { ["id"] = "A", ["type"] = "transmitter" });
                root["components"]!.AsArray().Add(new JsonObject { ["id"] = "B", ["type"] = "transmitter" });
                root["connections"]!.AsArray().Add(new JsonObject { ["from"] = "A.measurement", ["to"] = "B.value" });
                root["connections"]!.AsArray().Add(new JsonObject { ["from"] = "B.measurement", ["to"] = "A.value" });
            },
            "connections",
            ["laço algébrico", "A, B"]),

        ["combinação inválida de parâmetros"] = new(
            root => root["components"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "PSV-101",
                ["type"] = "reliefValve",
                ["params"] = new JsonObject { ["setPressurePa"] = 1.0e6, ["reseatPressurePa"] = 1.2e6, ["ratedFlowKgPerS"] = 5.0 },
            }),
            "components[4].params",
            ["fechamento", "ajuste"]),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void InvalidScenario_ReportsFieldPathAndClearMessage(string caseName)
    {
        InvalidCase invalid = Cases[caseName];
        JsonNode root = JsonNode.Parse(ScenarioFiles.ReadTest("water-tank.json"))!;
        invalid.Break(root);

        var exception = Assert.Throws<ScenarioValidationException>(() => ScenarioPlant.FromJson(root.ToJsonString()));

        output.WriteLine($"caso: {caseName}");
        foreach (ScenarioError error in exception.Errors)
        {
            output.WriteLine($"  {error}");
        }

        ScenarioError found = Assert.Single(exception.Errors, e => e.Path == invalid.ExpectedPath);
        foreach (string fragment in invalid.ExpectedFragments)
        {
            Assert.Contains(fragment, found.Message, StringComparison.Ordinal);
        }

        // O erro vem sozinho: um defeito não gera uma cascata de erros derivados.
        Assert.Single(exception.Errors);
        Assert.Contains(invalid.ExpectedPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AtLeastTenInvalidCases_AreCovered() => Assert.True(Cases.Count >= 10, $"casos: {Cases.Count}");

    /// <summary>Texto que nem é JSON: o erro aponta o documento (<c>$</c>), com linha e coluna.</summary>
    [Fact]
    public void MalformedJson_ReportsLineAndColumn()
    {
        var exception = Assert.Throws<ScenarioValidationException>(() => ScenarioPlant.FromJson("{\n  \"schemaVersion\": 1,\n  \"id\": \n}"));

        ScenarioError error = Assert.Single(exception.Errors);
        output.WriteLine(error.ToString());
        Assert.Equal("$", error.Path);
        Assert.Contains("não é JSON válido", error.Message, StringComparison.Ordinal);
        Assert.Contains("linha 4", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Vários defeitos independentes saem todos de uma vez, cada um com o seu caminho.</summary>
    [Fact]
    public void SeveralProblems_AreAllReported()
    {
        JsonNode root = JsonNode.Parse(ScenarioFiles.ReadTest("water-tank.json"))!;
        root["components"]![0]!["params"]!["ratedFlow"] = -0.03;
        root["components"]![2]!["params"]!["areaM2"] = 0.0;
        root["connections"]![2]!["to"] = "LT-101.entrada";
        root["outputs"]![0]!["from"] = "TQ-999.level";

        var exception = Assert.Throws<ScenarioValidationException>(() => ScenarioPlant.FromJson(root.ToJsonString()));

        output.WriteLine(exception.Message);
        Assert.Equal(
            ["components[0].params.ratedFlow", "components[2].params.areaM2", "connections[2].to", "outputs[0].from"],
            exception.Errors.Select(e => e.Path));
    }

    [Fact]
    public void ValidScenario_HasNoErrors()
    {
        var plant = ScenarioPlant.FromJson(ScenarioFiles.ReadTest("water-tank.json"));

        Assert.Equal("water-tank", plant.Definition.Id);
        Assert.Equal(4, plant.Components.Count);
        Assert.Equal(["pumpRun", "valveCommand", "outletOpening"], plant.InputNames);
        Assert.Equal(["level", "measuredLevel"], plant.OutputNames);
    }

    private sealed record InvalidCase(Action<JsonNode> Break, string ExpectedPath, string[] ExpectedFragments);
}
