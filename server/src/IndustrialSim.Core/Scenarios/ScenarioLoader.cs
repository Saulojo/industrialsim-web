using System.Globalization;
using System.Text.Json;

namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Lê o texto de um <c>scenario.json</c> e confere a estrutura: campos conhecidos, tipos JSON, nomes únicos e
/// a forma das referências <c>componente.sinal</c>. Não lê arquivo (o núcleo não faz I/O): recebe o texto.
/// Todos os erros são acumulados e saem juntos, cada um com o caminho do campo.
/// </summary>
public static partial class ScenarioLoader
{
    /// <summary>Versão do formato que este código lê.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly string[] RootProperties =
        ["$schema", "schemaVersion", "id", "description", "stepSeconds", "noiseSeed", "components", "connections", "inputs", "outputs", "io"];

    private static readonly string[] ComponentProperties = ["id", "type", "params", "initial"];
    private static readonly string[] ConnectionProperties = ["from", "to"];
    private static readonly string[] InputProperties = ["name", "to", "initial"];
    private static readonly string[] OutputProperties = ["name", "from"];

    /// <summary>Lê e confere a estrutura; lança <see cref="ScenarioValidationException"/> com todos os erros.</summary>
    public static ScenarioDefinition Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var errors = new List<ScenarioError>();
        ScenarioDefinition? definition = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            definition = ReadRoot(document.RootElement, errors);
        }
        catch (JsonException ex)
        {
            string where = ex.LineNumber is long line
                ? string.Create(CultureInfo.InvariantCulture, $" (linha {line + 1}, coluna {(ex.BytePositionInLine ?? 0) + 1})")
                : string.Empty;
            // A mensagem do .NET termina repetindo linha e posição (em base 0); a nossa já as traz em base 1.
            int suffix = ex.Message.IndexOf(" LineNumber:", StringComparison.Ordinal);
            string detail = suffix >= 0 ? ex.Message[..suffix] : ex.Message;
            errors.Add(new ScenarioError("$", $"o texto não é JSON válido{where}: {detail}"));
        }

        if (errors.Count > 0 || definition is null)
        {
            throw new ScenarioValidationException(errors);
        }

        return definition;
    }

    /// <summary>Separa <c>componente.sinal</c> no último ponto. Falso se faltar alguma das partes.</summary>
    public static bool TrySplitReference(string reference, out string componentId, out string signal)
    {
        componentId = string.Empty;
        signal = string.Empty;
        if (string.IsNullOrEmpty(reference))
        {
            return false;
        }

        int dot = reference.LastIndexOf('.');
        if (dot <= 0 || dot == reference.Length - 1)
        {
            return false;
        }

        componentId = reference[..dot];
        signal = reference[(dot + 1)..];
        return true;
    }

    private static ScenarioDefinition? ReadRoot(JsonElement root, List<ScenarioError> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ScenarioError("$", $"esperado um objeto; recebido {Describe(root)}."));
            return null;
        }

        CheckUnknownProperties(root, string.Empty, RootProperties, errors);

        int? version = ReadSchemaVersion(root, errors);
        if (version is not CurrentSchemaVersion)
        {
            // Com outra versão, os demais campos podem ter outro significado: não adianta conferi-los.
            return null;
        }

        string? id = ReadIdentifier(root, string.Empty, "id", errors);
        string? description = ReadString(root, string.Empty, "description", required: false, errors);

        double stepSeconds = Simulation.SimulationClock.DefaultStepSeconds;
        if (ReadNumber(root, string.Empty, "stepSeconds", required: false, errors) is double step)
        {
            if (step > 0.0)
            {
                stepSeconds = step;
            }
            else
            {
                errors.Add(new ScenarioError("stepSeconds", $"valor {Format(step)} fora da faixa: deve ser > 0 (segundos)."));
            }
        }

        ulong noiseSeed = 0;
        if (root.TryGetProperty("noiseSeed", out JsonElement seedElement))
        {
            if (seedElement.ValueKind != JsonValueKind.Number || !seedElement.TryGetUInt64(out noiseSeed))
            {
                errors.Add(new ScenarioError("noiseSeed", $"esperado um inteiro de 0 a 18446744073709551615; recebido {Describe(seedElement)}."));
            }
        }

        var components = ReadComponents(root, errors);
        var connections = ReadArray(root, "connections", required: false, errors, ReadConnection);
        var inputs = ReadArray(root, "inputs", required: false, errors, ReadInput);
        var outputs = ReadArray(root, "outputs", required: false, errors, ReadOutput);

        CheckUniqueExternalNames(inputs, outputs, errors);

        // Com erro em inputs ou outputs, os nomes ficam incompletos: a seção io não confere nomes, para não repetir o erro.
        IoDefinition? io = ReadIo(
            root,
            inputs.Contains(null) ? null : [.. inputs.Select(i => i!.Name)],
            outputs.Contains(null) ? null : [.. outputs.Select(o => o!.Name)],
            errors);

        if (errors.Count > 0)
        {
            return null;
        }

        return new ScenarioDefinition(
            CurrentSchemaVersion,
            id!,
            description,
            stepSeconds,
            noiseSeed,
            components.Select(c => c!).ToList(),
            connections.Select(c => c!).ToList(),
            inputs.Select(i => i!).ToList(),
            outputs.Select(o => o!).ToList(),
            io);
    }

    private static int? ReadSchemaVersion(JsonElement root, List<ScenarioError> errors)
    {
        if (!root.TryGetProperty("schemaVersion", out JsonElement element))
        {
            errors.Add(new ScenarioError("schemaVersion", $"campo obrigatório ausente; este código lê a versão {CurrentSchemaVersion}."));
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int version))
        {
            errors.Add(new ScenarioError("schemaVersion", $"esperado um inteiro; recebido {Describe(element)}."));
            return null;
        }

        if (version != CurrentSchemaVersion)
        {
            errors.Add(new ScenarioError("schemaVersion", $"versão {version} não suportada; este código lê a versão {CurrentSchemaVersion}."));
        }

        return version;
    }

    private static List<ComponentDefinition?> ReadComponents(JsonElement root, List<ScenarioError> errors)
    {
        var components = ReadArray(root, "components", required: true, errors, ReadComponent);
        if (root.TryGetProperty("components", out JsonElement array) && array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == 0)
        {
            errors.Add(new ScenarioError("components", "o cenário precisa de pelo menos um componente."));
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i] is not { } component)
            {
                continue;
            }

            if (seen.TryGetValue(component.Id, out int first))
            {
                errors.Add(new ScenarioError($"components[{i}].id", $"o identificador '{component.Id}' já foi usado em components[{first}]."));
            }
            else
            {
                seen.Add(component.Id, i);
            }
        }

        return components;
    }

    private static ComponentDefinition? ReadComponent(JsonElement element, string path, List<ScenarioError> errors)
    {
        int before = errors.Count;
        CheckUnknownProperties(element, path, ComponentProperties, errors);

        string? id = ReadIdentifier(element, path, "id", errors);
        string? type = ReadString(element, path, "type", required: true, errors);
        var parameters = ReadValues(element, path, "params", errors);
        var initial = ReadValues(element, path, "initial", errors);

        return errors.Count == before ? new ComponentDefinition(id!, type!, parameters, initial) : null;
    }

    private static ConnectionDefinition? ReadConnection(JsonElement element, string path, List<ScenarioError> errors)
    {
        int before = errors.Count;
        CheckUnknownProperties(element, path, ConnectionProperties, errors);

        string? from = ReadReference(element, path, "from", errors);
        string? to = ReadReference(element, path, "to", errors);

        return errors.Count == before ? new ConnectionDefinition(from!, to!) : null;
    }

    private static InputDefinition? ReadInput(JsonElement element, string path, List<ScenarioError> errors)
    {
        int before = errors.Count;
        CheckUnknownProperties(element, path, InputProperties, errors);

        string? name = ReadIdentifier(element, path, "name", errors);
        double initial = ReadNumber(element, path, "initial", required: false, errors) ?? 0.0;

        var targets = new List<string>();
        string toPath = Join(path, "to");
        if (!element.TryGetProperty("to", out JsonElement to))
        {
            errors.Add(new ScenarioError(toPath, "campo obrigatório ausente: a lista de entradas 'componente.sinal' que esta entrada alimenta."));
        }
        else if (to.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ScenarioError(toPath, $"esperada uma lista; recebido {Describe(to)}."));
        }
        else if (to.GetArrayLength() == 0)
        {
            errors.Add(new ScenarioError(toPath, "a lista não pode ser vazia."));
        }
        else
        {
            int index = 0;
            foreach (JsonElement item in to.EnumerateArray())
            {
                string itemPath = $"{toPath}[{index++}]";
                if (CheckReference(item, itemPath, errors) is string reference)
                {
                    targets.Add(reference);
                }
            }
        }

        return errors.Count == before ? new InputDefinition(name!, targets, initial) : null;
    }

    private static OutputDefinition? ReadOutput(JsonElement element, string path, List<ScenarioError> errors)
    {
        int before = errors.Count;
        CheckUnknownProperties(element, path, OutputProperties, errors);

        string? name = ReadIdentifier(element, path, "name", errors);
        string? from = ReadReference(element, path, "from", errors);

        return errors.Count == before ? new OutputDefinition(name!, from!) : null;
    }

    private static void CheckUniqueExternalNames(List<InputDefinition?> inputs, List<OutputDefinition?> outputs, List<ScenarioError> errors)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < inputs.Count; i++)
        {
            Check(inputs[i]?.Name, $"inputs[{i}].name");
        }

        for (int i = 0; i < outputs.Count; i++)
        {
            Check(outputs[i]?.Name, $"outputs[{i}].name");
        }

        void Check(string? name, string path)
        {
            if (name is null)
            {
                return;
            }

            if (seen.TryGetValue(name, out string? first))
            {
                errors.Add(new ScenarioError(path, $"o nome '{name}' já foi usado em {first}; entradas e saídas externas não podem repetir nomes."));
            }
            else
            {
                seen.Add(name, path);
            }
        }
    }

    private static List<T?> ReadArray<T>(
        JsonElement parent,
        string name,
        bool required,
        List<ScenarioError> errors,
        Func<JsonElement, string, List<ScenarioError>, T?> readItem)
        where T : class
    {
        var items = new List<T?>();
        if (!parent.TryGetProperty(name, out JsonElement array))
        {
            if (required)
            {
                errors.Add(new ScenarioError(name, "campo obrigatório ausente."));
            }

            return items;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ScenarioError(name, $"esperada uma lista; recebido {Describe(array)}."));
            return items;
        }

        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = $"{name}[{index++}]";
            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ScenarioError(path, $"esperado um objeto; recebido {Describe(element)}."));
                items.Add(null);
                continue;
            }

            items.Add(readItem(element, path, errors));
        }

        return items;
    }

    private static Dictionary<string, ScenarioValue> ReadValues(JsonElement parent, string parentPath, string name, List<ScenarioError> errors)
    {
        var values = new Dictionary<string, ScenarioValue>(StringComparer.Ordinal);
        if (!parent.TryGetProperty(name, out JsonElement element))
        {
            return values;
        }

        string path = Join(parentPath, name);
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ScenarioError(path, $"esperado um objeto; recebido {Describe(element)}."));
            return values;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            string valuePath = Join(path, property.Name);
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Number when property.Value.TryGetDouble(out double number) && double.IsFinite(number):
                    values[property.Name] = ScenarioValue.FromNumber(number);
                    break;
                case JsonValueKind.String:
                    values[property.Name] = ScenarioValue.FromText(property.Value.GetString()!);
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    values[property.Name] = ScenarioValue.FromBoolean(property.Value.GetBoolean());
                    break;
                default:
                    errors.Add(new ScenarioError(valuePath, $"esperado um número, um texto ou um booleano; recebido {Describe(property.Value)}."));
                    break;
            }
        }

        return values;
    }

    private static string? ReadString(JsonElement parent, string parentPath, string name, bool required, List<ScenarioError> errors)
    {
        string path = Join(parentPath, name);
        if (!parent.TryGetProperty(name, out JsonElement element))
        {
            if (required)
            {
                errors.Add(new ScenarioError(path, "campo obrigatório ausente."));
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(new ScenarioError(path, $"esperado um texto; recebido {Describe(element)}."));
            return null;
        }

        string value = element.GetString()!;
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ScenarioError(path, "o texto não pode ser vazio."));
            return null;
        }

        return value;
    }

    /// <summary>Identificador: texto não vazio, sem espaços e sem ponto (o ponto separa componente e sinal).</summary>
    private static string? ReadIdentifier(JsonElement parent, string parentPath, string name, List<ScenarioError> errors)
    {
        string? value = ReadString(parent, parentPath, name, required: true, errors);
        if (value is null)
        {
            return null;
        }

        if (value.Contains('.', StringComparison.Ordinal) || value.Any(char.IsWhiteSpace))
        {
            errors.Add(new ScenarioError(Join(parentPath, name), $"'{value}' não pode ter espaço nem ponto (o ponto separa componente e sinal)."));
            return null;
        }

        return value;
    }

    private static string? ReadReference(JsonElement parent, string parentPath, string name, List<ScenarioError> errors)
    {
        string path = Join(parentPath, name);
        if (!parent.TryGetProperty(name, out JsonElement element))
        {
            errors.Add(new ScenarioError(path, "campo obrigatório ausente: use 'componente.sinal'."));
            return null;
        }

        return CheckReference(element, path, errors);
    }

    private static string? CheckReference(JsonElement element, string path, List<ScenarioError> errors)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(new ScenarioError(path, $"esperado um texto no formato 'componente.sinal'; recebido {Describe(element)}."));
            return null;
        }

        string value = element.GetString()!;
        if (!TrySplitReference(value, out _, out _))
        {
            errors.Add(new ScenarioError(path, $"'{value}' não está no formato 'componente.sinal'."));
            return null;
        }

        return value;
    }

    private static double? ReadNumber(JsonElement parent, string parentPath, string name, bool required, List<ScenarioError> errors)
    {
        string path = Join(parentPath, name);
        if (!parent.TryGetProperty(name, out JsonElement element))
        {
            if (required)
            {
                errors.Add(new ScenarioError(path, "campo obrigatório ausente."));
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double value) || !double.IsFinite(value))
        {
            errors.Add(new ScenarioError(path, $"esperado um número; recebido {Describe(element)}."));
            return null;
        }

        return value;
    }

    private static void CheckUnknownProperties(JsonElement element, string path, string[] allowed, List<ScenarioError> errors)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                errors.Add(new ScenarioError(Join(path, property.Name), $"campo desconhecido. Aceitos aqui: {string.Join(", ", allowed)}."));
            }
        }
    }

    private static string Join(string parentPath, string name) => parentPath.Length == 0 ? name : $"{parentPath}.{name}";

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "um objeto",
        JsonValueKind.Array => "uma lista",
        JsonValueKind.String => $"o texto \"{element.GetString()}\"",
        JsonValueKind.Number => "o número " + element.GetRawText(),
        JsonValueKind.True => "o booleano true",
        JsonValueKind.False => "o booleano false",
        _ => "null",
    };
}
