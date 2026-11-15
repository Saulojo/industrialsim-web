using System.Text.Json.Nodes;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.Core.Tests.Scenarios;

/// <summary>
/// Etapa 4: leitura da seção <c>io</c> (mapa Modbus). Os casos inválidos partem do cenário da caldeira,
/// estragam um ponto e conferem o caminho do campo e a mensagem.
/// </summary>
public class IoSectionTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, InvalidCase> Cases = new()
    {
        ["campo desconhecido em io"] = new(
            io => io["registers"] = new JsonArray(),
            "io.registers",
            ["campo desconhecido", "holdingRegisters"]),

        ["endereço ausente"] = new(
            io => io["coils"]![0]!.AsObject().Remove("address"),
            "io.coils[0].address",
            ["campo obrigatório ausente", "base 0"]),

        ["endereço não inteiro"] = new(
            io => io["inputRegisters"]![0]!["address"] = 1.5,
            "io.inputRegisters[0].address",
            ["esperado um endereço inteiro"]),

        ["endereço fora da faixa"] = new(
            io => io["discreteInputs"]![0]!["address"] = 70000,
            "io.discreteInputs[0].address",
            ["endereço 70000 fora da faixa", "65535"]),

        ["endereço analógico que não cabe em float32"] = new(
            io => io["holdingRegisters"]![1]!["address"] = 40000,
            "io.holdingRegisters[1].address",
            ["endereço 40000 fora da faixa", "32767", "2a e 2a+1"]),

        ["endereço repetido"] = new(
            io => io["inputRegisters"]![3]!["address"] = 1,
            "io.inputRegisters[3].address",
            ["o endereço 1 já foi usado em io.inputRegisters[1]"]),

        ["saída externa inexistente"] = new(
            io => io["inputRegisters"]![0]!["signal"] = "LT-999",
            "io.inputRegisters[0].signal",
            ["saída externa inexistente: 'LT-999'", "LT-101"]),

        ["entrada externa inexistente"] = new(
            io => io["coils"]![0]!["signal"] = "P-101_running",
            "io.coils[0].signal",
            ["entrada externa inexistente: 'P-101_running'", "P-101_run"]),

        ["entrada comandada por dois pontos"] = new(
            io => io["holdingRegisters"]![1]!["signal"] = "FV-101_command",
            "io.holdingRegisters[1].signal",
            ["a entrada externa 'FV-101_command' já é comandada por io.holdingRegisters[0]"]),

        ["faixa nula"] = new(
            io => io["inputRegisters"]![1]!["urv"] = 0.0,
            "io.inputRegisters[1].urv",
            ["não pode ser igual ao início"]),

        ["fator de unidade zero"] = new(
            io => io["inputRegisters"]![1]!["unitFactor"] = 0.0,
            "io.inputRegisters[1].unitFactor",
            ["não pode ser 0"]),

        ["diagnóstico sobre os sinais em float32"] = new(
            io => io["diagnostics"]!["address"] = 8,
            "io.diagnostics.address",
            ["o bloco começa em 8", "até o endereço 11", "use 12 ou mais"]),

        ["diagnóstico sem valores"] = new(
            io => io["diagnostics"]!["values"] = new JsonArray(),
            "io.diagnostics.values",
            ["não pode ser vazia"]),

        ["heartbeat sobre os comandos em float32"] = new(
            io => io["watchdog"]!["heartbeatRegister"] = 3,
            "io.watchdog.heartbeatRegister",
            ["o heartbeat está em 3", "até o endereço 3", "use 4 ou mais"]),

        ["entrada do watchdog já usada"] = new(
            io => io["watchdog"]!["statusDiscreteInput"] = 4,
            "io.watchdog.statusDiscreteInput",
            ["o endereço 4 já foi usado em io.discreteInputs[4]"]),

        ["tempo do watchdog não positivo"] = new(
            io => io["watchdog"]!["timeoutSeconds"] = 0,
            "io.watchdog.timeoutSeconds",
            ["valor 0 fora da faixa", "> 0"]),

        ["estado seguro de entrada inexistente"] = new(
            io => io["watchdog"]!["safeState"]![0]!["input"] = "FV-999_command",
            "io.watchdog.safeState[0].input",
            ["entrada externa inexistente: 'FV-999_command'"]),

        ["estado seguro repetido"] = new(
            io => io["watchdog"]!["safeState"]![1]!["input"] = "FV-101_command",
            "io.watchdog.safeState[1].input",
            ["já tem valor seguro em io.watchdog.safeState[0]"]),

        ["io com tipo errado"] = new(
            io => io.Parent!["io"] = "mapa",
            "io",
            ["esperado um objeto"]),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void InvalidIoSection_ReportsFieldPathAndClearMessage(string caseName)
    {
        InvalidCase invalid = Cases[caseName];
        JsonNode root = JsonNode.Parse(ScenarioFiles.ReadProduct("caldeira/scenario.json"))!;
        invalid.Break(root["io"]!);

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

    /// <summary>Um cenário sem a seção <c>io</c> continua válido (a seção é opcional).</summary>
    [Fact]
    public void ScenarioWithoutIo_IsValid()
    {
        ScenarioDefinition definition = ScenarioLoader.Load(ScenarioFiles.ReadTest("water-tank.json"));

        Assert.Null(definition.Io);
    }

    /// <summary>Um erro em <c>outputs</c> não se repete em cada ponto da seção <c>io</c>.</summary>
    [Fact]
    public void ErrorInOutputs_IsNotRepeatedInIo()
    {
        JsonNode root = JsonNode.Parse(ScenarioFiles.ReadProduct("caldeira/scenario.json"))!;
        root["outputs"]![0]!["name"] = "LT 101";

        var exception = Assert.Throws<ScenarioValidationException>(() => ScenarioLoader.Load(root.ToJsonString()));

        ScenarioError error = Assert.Single(exception.Errors);
        Assert.Equal("outputs[0].name", error.Path);
    }

    /// <summary>O mapa da caldeira no arquivo é o do descritivo (seção "Mapa Modbus inicial").</summary>
    [Fact]
    public void CaldeiraIoSection_MatchesTheProjectDescription()
    {
        IoDefinition io = ScenarioLoader.Load(ScenarioFiles.ReadProduct("caldeira/scenario.json")).Io!;

        Assert.Equal([(0, "P-101_run")], io.Coils.Select(p => (p.Address, p.Signal)));
        Assert.Equal(
            [(0, "P-101_running"), (1, "LSLL-101"), (2, "LSHH-101"), (3, "PSH-101"), (4, "PSV-101_open")],
            io.DiscreteInputs.Select(p => (p.Address, p.Signal)));
        Assert.Equal(
            [(0, "FV-101_command"), (1, "FV-102_command")],
            io.HoldingRegisters.Select(p => (p.Address, p.Signal)));
        Assert.Equal(
            [(0, "LT-101"), (1, "PT-101"), (2, "FT-101"), (3, "FT-102"), (4, "FV-101_position"), (5, "FV-102_position")],
            io.InputRegisters.Select(p => (p.Address, p.Signal)));

        Assert.Equal(100, io.Diagnostics!.Address);
        Assert.Equal(
            [("LT-101", "m"), ("PT-101", "bar"), ("FT-101", "kg/s"), ("FT-102", "kg/s"), ("FV-103_position", "%")],
            io.Diagnostics.Values.Select(v => (v.Signal, v.Unit)));

        WatchdogDefinition watchdog = io.Watchdog!;
        Assert.Equal(10, watchdog.HeartbeatRegister);
        Assert.Equal(5, watchdog.StatusDiscreteInput);
        Assert.Equal(2.0, watchdog.TimeoutSeconds);
        Assert.Equal(
            [("FV-101_command", 1.0), ("FV-102_command", 0.0)],
            watchdog.SafeState.Select(s => (s.Input, s.Value)));
    }

    private sealed record InvalidCase(Action<JsonNode> Break, string ExpectedPath, string[] ExpectedFragments);
}
