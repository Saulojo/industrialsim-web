using IndustrialSim.Core.Components;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.Core.Tests.Scenarios;

public class ScenarioEngineTests(ITestOutputHelper output)
{
    /// <summary>
    /// Laço de realimentação com estado: uma válvula comandada pela medição (amortecida) da própria posição.
    /// É aceito; a ligação que fecha o laço entrega o valor do passo anterior ao primeiro componente do arquivo.
    /// </summary>
    [Fact]
    public void FeedbackLoopThroughState_IsAcceptedWithOneStepDelay()
    {
        const string Json = """
            {
              "schemaVersion": 1,
              "id": "loop",
              "components": [
                { "id": "V", "type": "valve", "params": { "timeConstantSeconds": 1.0 }, "initial": { "position": 0.8 } },
                { "id": "T", "type": "transmitter", "params": { "dampingSeconds": 0.5 } }
              ],
              "connections": [
                { "from": "V.position", "to": "T.value" },
                { "from": "T.measurement", "to": "V.command" }
              ]
            }
            """;

        var plant = ScenarioPlant.FromJson(Json);

        Assert.Equal(["V", "T"], plant.ExecutionOrderIds);
        Assert.Equal(0.8, plant.GetSignal("T.measurement")); // o filtro começa no valor medido

        // Em regime desde o início: comando = posição, nada se move.
        for (int n = 0; n < 1000; n++)
        {
            plant.Step();
        }

        Assert.Equal(0.8, plant.GetSignal("V.position"), 12);
    }

    /// <summary>
    /// Num laço com componentes sem estado, a ligação atrasada entra no componente com estado, onde quer que
    /// ele esteja no arquivo: os sem estado continuam vendo o valor do mesmo passo.
    /// </summary>
    [Fact]
    public void Loop_IsBrokenAtStatefulComponent()
    {
        const string Json = """
            {
              "schemaVersion": 1,
              "id": "loop",
              "components": [
                { "id": "T", "type": "transmitter" },
                { "id": "V", "type": "valve", "params": { "timeConstantSeconds": 1.0 } }
              ],
              "connections": [
                { "from": "V.position", "to": "T.value" },
                { "from": "T.measurement", "to": "V.command" }
              ]
            }
            """;

        var plant = ScenarioPlant.FromJson(Json);

        Assert.Equal(["V", "T"], plant.ExecutionOrderIds);
    }

    /// <summary>Componentes sem ligação entre si avançam na ordem do arquivo; quem fornece vem antes de quem consome.</summary>
    [Fact]
    public void ExecutionOrder_FollowsConnectionsThenFileOrder()
    {
        const string Json = """
            {
              "schemaVersion": 1,
              "id": "order",
              "components": [
                { "id": "LT", "type": "transmitter" },
                { "id": "TQ", "type": "tank", "params": { "areaM2": 1, "heightM": 1, "outletAreaM2": 0.01 } },
                { "id": "X", "type": "pump", "params": { "ratedFlow": 1 } },
                { "id": "P", "type": "pump", "params": { "ratedFlow": 0.01 } }
              ],
              "connections": [
                { "from": "TQ.level", "to": "LT.value" },
                { "from": "P.flow", "to": "TQ.inflow" }
              ]
            }
            """;

        var plant = ScenarioPlant.FromJson(Json);

        output.WriteLine(string.Join(" → ", plant.ExecutionOrderIds));
        Assert.Equal(["X", "P", "TQ", "LT"], plant.ExecutionOrderIds);
    }

    /// <summary>Uma entrada externa pode alimentar várias entradas de componentes.</summary>
    [Fact]
    public void ExternalInput_CanFeedSeveralComponents()
    {
        const string Json = """
            {
              "schemaVersion": 1,
              "id": "fanout",
              "components": [
                { "id": "A", "type": "valve", "params": { "timeConstantSeconds": 0.5 } },
                { "id": "B", "type": "valve", "params": { "timeConstantSeconds": 0.5 } }
              ],
              "inputs": [ { "name": "command", "to": ["A.command", "B.command"], "initial": 0.25 } ],
              "outputs": [ { "name": "a", "from": "A.position" }, { "name": "b", "from": "B.position" } ]
            }
            """;

        var plant = ScenarioPlant.FromJson(Json);
        for (int n = 0; n < 2000; n++)
        {
            plant.Step();
        }

        Assert.Equal(0.25, plant.GetInput("command"));
        Assert.Equal(0.25, plant.GetOutput("a"), 9);
        Assert.Equal(plant.GetOutput("a"), plant.GetOutput("b"));
    }

    [Fact]
    public void UnknownNames_ErrorListsAvailable()
    {
        var plant = ScenarioPlant.FromJson(ScenarioFiles.ReadTest("water-tank.json"));

        var input = Assert.Throws<KeyNotFoundException>(() => plant.SetInput("bomba", 1.0));
        var component = Assert.Throws<KeyNotFoundException>(() => plant.FindComponent("TQ-999"));

        Assert.Contains("pumpRun", input.Message, StringComparison.Ordinal);
        Assert.Contains("TQ-101", component.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => plant.GetSignal("TQ-101"));
    }

    /// <summary>
    /// D04: tipo novo de componente entra pelo registro, sem tocar no motor. Aqui, um integrador de teste.
    /// </summary>
    [Fact]
    public void CustomRegistry_AddsComponentTypeWithoutChangingEngine()
    {
        var registry = new ComponentRegistry(
        [
            .. StandardComponents.All,
            new ComponentType(
                "integrator",
                "Integrador de teste.",
                [ParameterSpec.Number("gain", "ganho", defaultValue: 1.0)],
                [],
                context => new Integrator(context.Id, context.Parameters.Number("gain"))),
        ]);

        const string Json = """
            {
              "schemaVersion": 1,
              "id": "custom",
              "components": [ { "id": "I", "type": "integrator", "params": { "gain": 2.0 } } ],
              "inputs": [ { "name": "u", "to": ["I.rate"], "initial": 1.0 } ],
              "outputs": [ { "name": "y", "from": "I.value" } ]
            }
            """;

        var plant = ScenarioPlant.FromJson(Json, registry);
        for (int n = 0; n < 100; n++)
        {
            plant.Step();
        }

        Assert.Equal(2.0, plant.GetOutput("y"), 12);
        Assert.Throws<ScenarioValidationException>(() => ScenarioPlant.FromJson(Json)); // o registro padrão não o conhece
    }

    /// <summary>
    /// RNF09: o arquivo <c>scenarios/scenario.schema.json</c> é exatamente o que o gerador produz a partir do
    /// registro de tipos. Se um tipo ou parâmetro mudar, este teste lembra de regerar o esquema
    /// (<c>simrun --esquema scenarios/scenario.schema.json</c>).
    /// </summary>
    [Fact]
    public void SchemaFile_MatchesGeneratedSchema()
    {
        string generated = ScenarioSchemaWriter.Write();
        string onDisk = ScenarioFiles.ReadProduct("scenario.schema.json").ReplaceLineEndings("\n");

        Assert.Equal(generated, onDisk);

        using var document = System.Text.Json.JsonDocument.Parse(generated);
        var types = document.RootElement.GetProperty("$defs").GetProperty("component").GetProperty("oneOf")
            .EnumerateArray().Select(t => t.GetProperty("properties").GetProperty("type").GetProperty("const").GetString());
        Assert.Equal(ComponentRegistry.Default.Types.Select(t => t.Name), types);
    }

    private sealed class Integrator : Component
    {
        private readonly double _gain;

        public Integrator(string name, double gain)
            : base(name)
        {
            _gain = gain;
            Inputs.Add("rate");
            StateSignals.Add("value");
            OutputSignals.Add("value");
        }

        protected override void Advance(double timeSeconds, double stepSeconds) =>
            StateSignals[0] += _gain * Inputs[0] * stepSeconds;

        protected override void ComputeOutputs() => OutputSignals[0] = StateSignals[0];
    }
}
