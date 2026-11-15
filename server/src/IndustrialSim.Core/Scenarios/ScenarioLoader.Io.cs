using System.Globalization;
using System.Text.Json;

namespace IndustrialSim.Core.Scenarios;

/// <summary>Leitura e conferência da seção <c>io</c> (mapa Modbus) do cenário.</summary>
public static partial class ScenarioLoader
{
    /// <summary>Maior endereço Modbus (base 0).</summary>
    public const int MaxAddress = 65535;

    /// <summary>
    /// Maior endereço de um sinal analógico: no formato <c>float32</c> ele ocupa <c>2a</c> e <c>2a+1</c> (D27).
    /// </summary>
    public const int MaxAnalogAddress = 32767;

    /// <summary>Tempo padrão do watchdog, em segundos (descritivo, mapa Modbus).</summary>
    public const double DefaultWatchdogTimeoutSeconds = 2.0;

    private static readonly string[] IoProperties =
        ["coils", "discreteInputs", "holdingRegisters", "inputRegisters", "diagnostics", "watchdog"];

    private static readonly string[] DigitalPointProperties = ["address", "signal"];
    private static readonly string[] AnalogPointProperties = ["address", "signal", "lrv", "urv", "unit", "unitFactor"];
    private static readonly string[] DiagnosticsProperties = ["address", "values"];
    private static readonly string[] DiagnosticValueProperties = ["signal", "unit", "unitFactor"];
    private static readonly string[] WatchdogProperties = ["heartbeatRegister", "statusDiscreteInput", "timeoutSeconds", "safeState"];
    private static readonly string[] SafeStateProperties = ["input", "value"];

    /// <summary>
    /// Lê a seção <c>io</c>. Nulo se ela não existir ou tiver erro (já registrado).
    /// </summary>
    /// <param name="root">Raiz do documento.</param>
    /// <param name="inputNames">Nomes das entradas externas; nulo se a lista <c>inputs</c> tiver erro (não conferir nomes).</param>
    /// <param name="outputNames">Nomes das saídas externas; nulo se a lista <c>outputs</c> tiver erro.</param>
    /// <param name="errors">Onde os erros são acumulados.</param>
    private static IoDefinition? ReadIo(
        JsonElement root,
        IReadOnlyList<string>? inputNames,
        IReadOnlyList<string>? outputNames,
        List<ScenarioError> errors)
    {
        if (!root.TryGetProperty("io", out JsonElement io))
        {
            return null;
        }

        if (io.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ScenarioError("io", $"esperado um objeto; recebido {Describe(io)}."));
            return null;
        }

        int before = errors.Count;
        CheckUnknownProperties(io, "io", IoProperties, errors);

        var commanded = new Dictionary<string, string>(StringComparer.Ordinal);
        var coils = ReadPoints(io, "coils", errors, (e, p) => ReadDigitalPoint(e, p, inputNames, "entrada externa", commanded, errors));
        var discreteInputs = ReadPoints(io, "discreteInputs", errors, (e, p) => ReadDigitalPoint(e, p, outputNames, "saída externa", null, errors));
        var holdingRegisters = ReadPoints(io, "holdingRegisters", errors, (e, p) => ReadAnalogPoint(e, p, inputNames, "entrada externa", commanded, errors));
        var inputRegisters = ReadPoints(io, "inputRegisters", errors, (e, p) => ReadAnalogPoint(e, p, outputNames, "saída externa", null, errors));

        CheckUniqueAddresses(coils, "io.coils", p => p.Address, errors);
        CheckUniqueAddresses(discreteInputs, "io.discreteInputs", p => p.Address, errors);
        CheckUniqueAddresses(holdingRegisters, "io.holdingRegisters", p => p.Address, errors);
        CheckUniqueAddresses(inputRegisters, "io.inputRegisters", p => p.Address, errors);

        DiagnosticsDefinition? diagnostics = ReadDiagnostics(io, outputNames, inputRegisters, errors);
        WatchdogDefinition? watchdog = ReadWatchdog(io, inputNames, holdingRegisters, discreteInputs, errors);

        if (errors.Count > before)
        {
            return null;
        }

        return new IoDefinition(
            [.. coils.Select(p => p!)],
            [.. discreteInputs.Select(p => p!)],
            [.. holdingRegisters.Select(p => p!)],
            [.. inputRegisters.Select(p => p!)],
            diagnostics,
            watchdog);
    }

    private static List<T?> ReadPoints<T>(JsonElement io, string name, List<ScenarioError> errors, Func<JsonElement, string, T?> readItem)
        where T : class
    {
        var items = new List<T?>();
        string listPath = $"io.{name}";
        if (!io.TryGetProperty(name, out JsonElement array))
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

    private static DigitalPointDefinition? ReadDigitalPoint(
        JsonElement element,
        string path,
        IReadOnlyList<string>? names,
        string kind,
        Dictionary<string, string>? commanded,
        List<ScenarioError> errors)
    {
        int before = errors.Count;
        CheckUnknownProperties(element, path, DigitalPointProperties, errors);

        int? address = ReadAddress(element, path, "address", required: true, MaxAddress, errors);
        string? signal = ReadSignalName(element, path, "signal", names, kind, errors);
        ClaimCommand(commanded, signal, path, errors);

        return errors.Count == before ? new DigitalPointDefinition(address!.Value, signal!) : null;
    }

    private static AnalogPointDefinition? ReadAnalogPoint(
        JsonElement element,
        string path,
        IReadOnlyList<string>? names,
        string kind,
        Dictionary<string, string>? commanded,
        List<ScenarioError> errors)
    {
        int before = errors.Count;
        CheckUnknownProperties(element, path, AnalogPointProperties, errors);

        int? address = ReadAddress(element, path, "address", required: true, MaxAnalogAddress, errors);
        string? signal = ReadSignalName(element, path, "signal", names, kind, errors);
        ClaimCommand(commanded, signal, path, errors);

        double lrv = ReadNumber(element, path, "lrv", required: false, errors) ?? 0.0;
        double urv = ReadNumber(element, path, "urv", required: false, errors) ?? 1.0;
        if (errors.Count == before && urv == lrv)
        {
            errors.Add(new ScenarioError(Join(path, "urv"), $"o fim da faixa (urv = {Format(urv)}) não pode ser igual ao início (lrv)."));
        }

        string unit = ReadString(element, path, "unit", required: false, errors) ?? string.Empty;
        double unitFactor = ReadUnitFactor(element, path, errors);

        return errors.Count == before ? new AnalogPointDefinition(address!.Value, signal!, lrv, urv, unit, unitFactor) : null;
    }

    private static DiagnosticsDefinition? ReadDiagnostics(
        JsonElement io,
        IReadOnlyList<string>? outputNames,
        List<AnalogPointDefinition?> inputRegisters,
        List<ScenarioError> errors)
    {
        const string Path = "io.diagnostics";
        if (!io.TryGetProperty("diagnostics", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ScenarioError(Path, $"esperado um objeto; recebido {Describe(element)}."));
            return null;
        }

        int before = errors.Count;
        CheckUnknownProperties(element, Path, DiagnosticsProperties, errors);
        int? address = ReadAddress(element, Path, "address", required: true, MaxAddress, errors);

        var values = new List<DiagnosticValueDefinition>();
        string valuesPath = Join(Path, "values");
        if (!element.TryGetProperty("values", out JsonElement array))
        {
            errors.Add(new ScenarioError(valuesPath, "campo obrigatório ausente."));
        }
        else if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ScenarioError(valuesPath, $"esperada uma lista; recebido {Describe(array)}."));
        }
        else if (array.GetArrayLength() == 0)
        {
            errors.Add(new ScenarioError(valuesPath, "a lista não pode ser vazia."));
        }
        else
        {
            int index = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                string itemPath = $"{valuesPath}[{index++}]";
                if (item.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new ScenarioError(itemPath, $"esperado um objeto; recebido {Describe(item)}."));
                    continue;
                }

                int beforeItem = errors.Count;
                CheckUnknownProperties(item, itemPath, DiagnosticValueProperties, errors);
                string? signal = ReadSignalName(item, itemPath, "signal", outputNames, "saída externa", errors);
                string unit = ReadString(item, itemPath, "unit", required: false, errors) ?? string.Empty;
                double unitFactor = ReadUnitFactor(item, itemPath, errors);
                if (errors.Count == beforeItem)
                {
                    values.Add(new DiagnosticValueDefinition(signal!, unit, unitFactor));
                }
            }
        }

        if (errors.Count > before)
        {
            return null;
        }

        // O bloco não pode cair sobre os sinais analógicos em nenhum dos dois arranjos de endereços (D27).
        int analogLength = IoDefinition.AnalogBlockLength([.. inputRegisters.Where(p => p is not null).Select(p => p!)]);
        if (address!.Value < analogLength)
        {
            errors.Add(new ScenarioError(
                Join(Path, "address"),
                $"o bloco começa em {address.Value}, mas os sinais dos input registers ocupam até o endereço {analogLength - 1} " +
                $"no formato float32 (o sinal de endereço a ocupa 2a e 2a+1); use {analogLength} ou mais."));
            return null;
        }

        int last = address.Value + (2 * values.Count) - 1;
        if (last > MaxAddress)
        {
            errors.Add(new ScenarioError(
                Join(Path, "address"),
                $"o bloco de {values.Count} valores (2 registradores cada) terminaria em {last}; o maior endereço é {MaxAddress}."));
            return null;
        }

        return new DiagnosticsDefinition(address.Value, values);
    }

    private static WatchdogDefinition? ReadWatchdog(
        JsonElement io,
        IReadOnlyList<string>? inputNames,
        List<AnalogPointDefinition?> holdingRegisters,
        List<DigitalPointDefinition?> discreteInputs,
        List<ScenarioError> errors)
    {
        const string Path = "io.watchdog";
        if (!io.TryGetProperty("watchdog", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ScenarioError(Path, $"esperado um objeto; recebido {Describe(element)}."));
            return null;
        }

        int before = errors.Count;
        CheckUnknownProperties(element, Path, WatchdogProperties, errors);

        int? heartbeat = ReadAddress(element, Path, "heartbeatRegister", required: true, MaxAddress, errors);
        int? status = ReadAddress(element, Path, "statusDiscreteInput", required: false, MaxAddress, errors);

        double timeout = DefaultWatchdogTimeoutSeconds;
        if (ReadNumber(element, Path, "timeoutSeconds", required: false, errors) is double seconds)
        {
            if (seconds > 0.0)
            {
                timeout = seconds;
            }
            else
            {
                errors.Add(new ScenarioError(Join(Path, "timeoutSeconds"), $"valor {Format(seconds)} fora da faixa: deve ser > 0 (segundos)."));
            }
        }

        var safeState = new List<SafeStateDefinition>();
        string safePath = Join(Path, "safeState");
        if (!element.TryGetProperty("safeState", out JsonElement array))
        {
            errors.Add(new ScenarioError(safePath, "campo obrigatório ausente: a lista de entradas externas e seus valores seguros."));
        }
        else if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ScenarioError(safePath, $"esperada uma lista; recebido {Describe(array)}."));
        }
        else
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                string itemPath = $"{safePath}[{index++}]";
                if (item.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new ScenarioError(itemPath, $"esperado um objeto; recebido {Describe(item)}."));
                    continue;
                }

                int beforeItem = errors.Count;
                CheckUnknownProperties(item, itemPath, SafeStateProperties, errors);
                string? input = ReadSignalName(item, itemPath, "input", inputNames, "entrada externa", errors);
                double? value = ReadNumber(item, itemPath, "value", required: true, errors);
                if (input is not null && seen.TryGetValue(input, out string? first))
                {
                    errors.Add(new ScenarioError(Join(itemPath, "input"), $"a entrada externa '{input}' já tem valor seguro em {first}."));
                }
                else if (input is not null)
                {
                    seen.Add(input, itemPath);
                }

                if (errors.Count == beforeItem)
                {
                    safeState.Add(new SafeStateDefinition(input!, value!.Value));
                }
            }
        }

        if (errors.Count > before)
        {
            return null;
        }

        int analogLength = IoDefinition.AnalogBlockLength([.. holdingRegisters.Where(p => p is not null).Select(p => p!)]);
        if (heartbeat!.Value < analogLength)
        {
            errors.Add(new ScenarioError(
                Join(Path, "heartbeatRegister"),
                $"o heartbeat está em {heartbeat.Value}, mas os sinais dos holding registers ocupam até o endereço {analogLength - 1} " +
                $"no formato float32 (o sinal de endereço a ocupa 2a e 2a+1); use {analogLength} ou mais."));
            return null;
        }

        if (status is int statusAddress)
        {
            int taken = discreteInputs.FindIndex(p => p is not null && p.Address == statusAddress);
            if (taken >= 0)
            {
                errors.Add(new ScenarioError(
                    Join(Path, "statusDiscreteInput"),
                    $"o endereço {statusAddress} já foi usado em io.discreteInputs[{taken}]."));
                return null;
            }
        }

        return new WatchdogDefinition(heartbeat.Value, status, timeout, safeState);
    }

    private static int? ReadAddress(JsonElement parent, string parentPath, string name, bool required, int maximum, List<ScenarioError> errors)
    {
        string path = Join(parentPath, name);
        if (!parent.TryGetProperty(name, out JsonElement element))
        {
            if (required)
            {
                errors.Add(new ScenarioError(path, "campo obrigatório ausente: o endereço Modbus, em base 0."));
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int address))
        {
            errors.Add(new ScenarioError(path, $"esperado um endereço inteiro (base 0); recebido {Describe(element)}."));
            return null;
        }

        if (address < 0 || address > maximum)
        {
            string reason = maximum == MaxAnalogAddress
                ? " (no formato float32 o sinal de endereço a ocupa 2a e 2a+1)"
                : string.Empty;
            errors.Add(new ScenarioError(
                path,
                string.Create(CultureInfo.InvariantCulture, $"endereço {address} fora da faixa: deve ficar entre 0 e {maximum}{reason}.")));
            return null;
        }

        return address;
    }

    /// <summary>Nome de uma entrada ou saída externa; confere se ela existe quando a lista de nomes é conhecida.</summary>
    private static string? ReadSignalName(
        JsonElement parent,
        string parentPath,
        string name,
        IReadOnlyList<string>? names,
        string kind,
        List<ScenarioError> errors)
    {
        string? value = ReadString(parent, parentPath, name, required: true, errors);
        if (value is null || names is null || names.Contains(value, StringComparer.Ordinal))
        {
            return value;
        }

        string available = names.Count == 0 ? "nenhuma" : string.Join(", ", names);
        errors.Add(new ScenarioError(Join(parentPath, name), $"{kind} inexistente: '{value}'. Disponíveis: {available}."));
        return null;
    }

    private static double ReadUnitFactor(JsonElement parent, string parentPath, List<ScenarioError> errors)
    {
        double factor = ReadNumber(parent, parentPath, "unitFactor", required: false, errors) ?? 1.0;
        if (factor == 0.0)
        {
            errors.Add(new ScenarioError(Join(parentPath, "unitFactor"), "o fator da unidade não pode ser 0."));
        }

        return factor;
    }

    /// <summary>Uma entrada externa só pode ser comandada por um ponto (coil ou holding register).</summary>
    private static void ClaimCommand(Dictionary<string, string>? commanded, string? signal, string path, List<ScenarioError> errors)
    {
        if (commanded is null || signal is null)
        {
            return;
        }

        if (commanded.TryGetValue(signal, out string? first))
        {
            errors.Add(new ScenarioError(Join(path, "signal"), $"a entrada externa '{signal}' já é comandada por {first}."));
        }
        else
        {
            commanded.Add(signal, path);
        }
    }

    private static void CheckUniqueAddresses<T>(List<T?> points, string listPath, Func<T, int> address, List<ScenarioError> errors)
        where T : class
    {
        var seen = new Dictionary<int, int>();
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] is not { } point)
            {
                continue;
            }

            if (seen.TryGetValue(address(point), out int first))
            {
                errors.Add(new ScenarioError($"{listPath}[{i}].address", $"o endereço {address(point)} já foi usado em {listPath}[{first}]."));
            }
            else
            {
                seen.Add(address(point), i);
            }
        }
    }
}
