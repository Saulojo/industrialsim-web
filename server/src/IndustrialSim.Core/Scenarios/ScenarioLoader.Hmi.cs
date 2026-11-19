using System.Text.Json;

namespace IndustrialSim.Core.Scenarios;

/// <summary>Leitura e conferência da seção <c>hmi</c> (distúrbios e alarmes da IHM web, D39).</summary>
public static partial class ScenarioLoader
{
    private static readonly string[] HmiProperties = ["disturbances", "alarms"];
    private static readonly string[] DisturbanceProperties = ["input", "min", "max", "unit", "unitFactor"];
    private static readonly string[] AlarmProperties = ["signal"];

    /// <summary>Lê a seção <c>hmi</c>. Nulo se ela não existir ou tiver erro (já registrado).</summary>
    /// <param name="root">Raiz do documento.</param>
    /// <param name="inputNames">Nomes das entradas externas; nulo se a lista <c>inputs</c> tiver erro.</param>
    /// <param name="outputNames">Nomes das saídas externas; nulo se a lista <c>outputs</c> tiver erro.</param>
    /// <param name="io">Mapa Modbus, para recusar distúrbio numa entrada que o mestre comanda.</param>
    /// <param name="errors">Onde os erros são acumulados.</param>
    private static HmiDefinition? ReadHmi(
        JsonElement root,
        IReadOnlyList<string>? inputNames,
        IReadOnlyList<string>? outputNames,
        IoDefinition? io,
        List<ScenarioError> errors)
    {
        if (!root.TryGetProperty("hmi", out JsonElement hmi))
        {
            return null;
        }

        if (hmi.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ScenarioError("hmi", $"esperado um objeto; recebido {Describe(hmi)}."));
            return null;
        }

        int before = errors.Count;
        CheckUnknownProperties(hmi, "hmi", HmiProperties, errors);

        var commanded = new HashSet<string>(StringComparer.Ordinal);
        if (io is not null)
        {
            commanded.UnionWith(io.Coils.Select(p => p.Signal));
            commanded.UnionWith(io.HoldingRegisters.Select(p => p.Signal));
        }

        var disturbances = ReadHmiList(hmi, "disturbances", errors, (e, p) => ReadDisturbance(e, p, inputNames, commanded, errors));
        var alarms = ReadHmiList(hmi, "alarms", errors, (e, p) => ReadAlarm(e, p, outputNames, errors));

        CheckUniqueNames(disturbances, "hmi.disturbances", "input", d => d.Input, errors);
        CheckUniqueNames(alarms, "hmi.alarms", "signal", a => a.Signal, errors);

        if (errors.Count > before)
        {
            return null;
        }

        return new HmiDefinition([.. disturbances.Select(d => d!)], [.. alarms.Select(a => a!)]);
    }

    private static List<T?> ReadHmiList<T>(JsonElement hmi, string name, List<ScenarioError> errors, Func<JsonElement, string, T?> readItem)
        where T : class
    {
        var items = new List<T?>();
        string listPath = $"hmi.{name}";
        if (!hmi.TryGetProperty(name, out JsonElement array))
        {
            return items;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ScenarioError(listPath, $"esperada uma lista; recebido {Describe(array)}."));
            return items;
        }

        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = $"{listPath}[{index++}]";
            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ScenarioError(path, $"esperado um objeto; recebido {Describe(element)}."));
                items.Add(null);
                continue;
            }

            items.Add(readItem(element, path));
        }

        return items;
    }

    private static DisturbanceDefinition? ReadDisturbance(
        JsonElement element,
        string path,
        IReadOnlyList<string>? inputNames,
        HashSet<string> commanded,
        List<ScenarioError> errors)
    {
        int before = errors.Count;
        CheckUnknownProperties(element, path, DisturbanceProperties, errors);

        string? input = ReadSignalName(element, path, "input", inputNames, "entrada externa", errors);
        if (input is not null && commanded.Contains(input))
        {
            errors.Add(new ScenarioError(
                Join(path, "input"),
                $"a entrada externa '{input}' é comandada pelo mestre no mapa Modbus; um distúrbio da IHM seria sobrescrito a cada passo."));
        }

        double min = ReadNumber(element, path, "min", required: false, errors) ?? 0.0;
        double max = ReadNumber(element, path, "max", required: false, errors) ?? 1.0;
        if (min >= max)
        {
            errors.Add(new ScenarioError(Join(path, "max"), $"o máximo ({Format(max)}) deve ser maior que o mínimo ({Format(min)})."));
        }

        string unit = ReadString(element, path, "unit", required: false, errors) ?? string.Empty;
        double unitFactor = ReadUnitFactor(element, path, errors);

        return errors.Count == before ? new DisturbanceDefinition(input!, min, max, unit, unitFactor) : null;
    }

    private static AlarmDefinition? ReadAlarm(JsonElement element, string path, IReadOnlyList<string>? outputNames, List<ScenarioError> errors)
    {
        int before = errors.Count;
        CheckUnknownProperties(element, path, AlarmProperties, errors);

        string? signal = ReadSignalName(element, path, "signal", outputNames, "saída externa", errors);

        return errors.Count == before ? new AlarmDefinition(signal!) : null;
    }

    private static void CheckUniqueNames<T>(List<T?> items, string listPath, string field, Func<T, string> name, List<ScenarioError> errors)
        where T : class
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not { } item)
            {
                continue;
            }

            if (seen.TryGetValue(name(item), out int first))
            {
                errors.Add(new ScenarioError($"{listPath}[{i}].{field}", $"'{name(item)}' já foi usado em {listPath}[{first}]."));
            }
            else
            {
                seen.Add(name(item), i);
            }
        }
    }
}
