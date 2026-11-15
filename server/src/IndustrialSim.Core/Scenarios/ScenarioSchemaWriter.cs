using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Gera o esquema JSON (JSON Schema, draft 2020-12) do <c>scenario.json</c> a partir do registro de tipos (RNF09).
/// O arquivo <c>scenarios/scenario.schema.json</c> é a saída deste gerador, e um teste garante que os dois
/// não divergem. O esquema serve ao editor (autocompletar e avisos); quem valida de fato, com mensagens em
/// português e o caminho do campo, são <see cref="ScenarioLoader"/> e <see cref="ScenarioPlant"/>.
/// </summary>
public static partial class ScenarioSchemaWriter
{
    private const string ReferencePattern = "^.+\\.[^.]+$";
    private const string IdentifierPattern = "^[^.\\s]+$";

    public static string Write(ComponentRegistry? registry = null)
    {
        registry ??= ComponentRegistry.Default;

        using var stream = new MemoryStream();
        var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" };
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
            writer.WriteString("title", "Cenário do IndustrialSim Web");
            writer.WriteString(
                "description",
                $"Formato do scenario.json, versão {ScenarioLoader.CurrentSchemaVersion}. Gerado por ScenarioSchemaWriter; não edite à mão.");
            writer.WriteString("type", "object");
            writer.WriteBoolean("additionalProperties", false);
            WriteStringArray(writer, "required", ["schemaVersion", "id", "components"]);

            writer.WriteStartObject("properties");
            WriteSimple(writer, "$schema", "string", "Caminho deste esquema, para o editor.");
            writer.WriteStartObject("schemaVersion");
            writer.WriteNumber("const", ScenarioLoader.CurrentSchemaVersion);
            writer.WriteString("description", "Versão do formato.");
            writer.WriteEndObject();
            WriteIdentifier(writer, "id", "Identificador do cenário.");
            WriteSimple(writer, "description", "string", "Nota para quem edita o arquivo; não aparece na interface.");
            writer.WriteStartObject("stepSeconds");
            writer.WriteString("type", "number");
            writer.WriteNumber("exclusiveMinimum", 0);
            writer.WriteNumber("default", Simulation.SimulationClock.DefaultStepSeconds);
            writer.WriteString("description", "Passo fixo de integração, em segundos.");
            writer.WriteEndObject();
            writer.WriteStartObject("noiseSeed");
            writer.WriteString("type", "integer");
            writer.WriteNumber("minimum", 0);
            writer.WriteNumber("default", 0);
            writer.WriteString("description", "Semente do ruído dos instrumentos.");
            writer.WriteEndObject();

            WriteArrayOf(writer, "components", "component", "Componentes da planta. O id é a tag do equipamento.", minItems: 1);
            WriteArrayOf(writer, "connections", "connection", "Ligações da saída de um componente à entrada de outro.");
            WriteArrayOf(writer, "inputs", "input", "Entradas externas da planta: comandos e cargas.");
            WriteArrayOf(writer, "outputs", "output", "Saídas externas da planta: medições e estados.");
            WriteIoProperty(writer);
            writer.WriteEndObject();

            writer.WriteStartObject("$defs");
            WriteReferenceDefinition(writer);
            WriteConnectionDefinition(writer);
            WriteInputDefinition(writer);
            WriteOutputDefinition(writer);
            WriteIoDefinitions(writer);

            writer.WriteStartObject("component");
            writer.WriteStartArray("oneOf");
            foreach (ComponentType type in registry.Types)
            {
                WriteComponentType(writer, type);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteComponentType(Utf8JsonWriter writer, ComponentType type)
    {
        bool paramsRequired = type.Parameters.Any(p => p.IsRequired);

        writer.WriteStartObject();
        writer.WriteString("title", type.Name);
        writer.WriteString("description", type.Description);
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", paramsRequired ? ["id", "type", "params"] : ["id", "type"]);

        writer.WriteStartObject("properties");
        WriteIdentifier(writer, "id", "Identificador do componente (a tag).");
        writer.WriteStartObject("type");
        writer.WriteString("const", type.Name);
        writer.WriteEndObject();
        WriteParameterGroup(writer, "params", type.Parameters);
        WriteParameterGroup(writer, "initial", type.Initial);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteParameterGroup(Utf8JsonWriter writer, string name, IReadOnlyList<ParameterSpec> specs)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        string[] required = [.. specs.Where(s => s.IsRequired).Select(s => s.Name)];
        if (required.Length > 0)
        {
            WriteStringArray(writer, "required", required);
        }

        writer.WriteStartObject("properties");
        foreach (ParameterSpec spec in specs)
        {
            writer.WriteStartObject(spec.Name);
            writer.WriteString("description", spec.Unit.Length == 0 ? spec.Description : $"{spec.Description} [{spec.Unit}]");
            if (spec.Kind == ParameterKind.Choice)
            {
                WriteStringArray(writer, "enum", spec.Choices);
                if (spec.DefaultChoice is not null)
                {
                    writer.WriteString("default", spec.DefaultChoice);
                }
            }
            else
            {
                writer.WriteString("type", "number");
                if (spec.Minimum is double min)
                {
                    writer.WriteNumber(spec.ExclusiveMinimum ? "exclusiveMinimum" : "minimum", min);
                }

                if (spec.Maximum is double max)
                {
                    writer.WriteNumber(spec.ExclusiveMaximum ? "exclusiveMaximum" : "maximum", max);
                }

                if (spec.Default is double defaultValue)
                {
                    writer.WriteNumber("default", defaultValue);
                }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteReferenceDefinition(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("reference");
        writer.WriteString("type", "string");
        writer.WriteString("pattern", ReferencePattern);
        writer.WriteString("description", "Sinal de um componente, no formato 'componente.sinal' (ex.: 'P-101.flow').");
        writer.WriteEndObject();
    }

    private static void WriteConnectionDefinition(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("connection");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["from", "to"]);
        writer.WriteStartObject("properties");
        WriteReference(writer, "from", "Saída que fornece o sinal.");
        WriteReference(writer, "to", "Entrada que recebe o sinal. Cada entrada só pode ter uma fonte.");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteInputDefinition(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("input");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["name", "to"]);
        writer.WriteStartObject("properties");
        WriteIdentifier(writer, "name", "Nome da entrada externa.");
        writer.WriteStartObject("to");
        writer.WriteString("type", "array");
        writer.WriteNumber("minItems", 1);
        writer.WriteStartObject("items");
        writer.WriteString("$ref", "#/$defs/reference");
        writer.WriteEndObject();
        writer.WriteString("description", "Entradas de componentes que esta entrada alimenta.");
        writer.WriteEndObject();
        writer.WriteStartObject("initial");
        writer.WriteString("type", "number");
        writer.WriteNumber("default", 0);
        writer.WriteString("description", "Valor no instante inicial.");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteOutputDefinition(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("output");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["name", "from"]);
        writer.WriteStartObject("properties");
        WriteIdentifier(writer, "name", "Nome da saída externa.");
        WriteReference(writer, "from", "Saída de componente publicada com esse nome.");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteArrayOf(Utf8JsonWriter writer, string name, string definition, string description, int minItems = 0)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "array");
        if (minItems > 0)
        {
            writer.WriteNumber("minItems", minItems);
        }

        writer.WriteStartObject("items");
        writer.WriteString("$ref", $"#/$defs/{definition}");
        writer.WriteEndObject();
        writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteSimple(Utf8JsonWriter writer, string name, string type, string description)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", type);
        writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteIdentifier(Utf8JsonWriter writer, string name, string description)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "string");
        writer.WriteString("pattern", IdentifierPattern);
        writer.WriteString("description", description + " Sem espaço e sem ponto.");
        writer.WriteEndObject();
    }

    private static void WriteReference(Utf8JsonWriter writer, string name, string description)
    {
        writer.WriteStartObject(name);
        writer.WriteString("$ref", "#/$defs/reference");
        writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
