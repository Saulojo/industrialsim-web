using System.Globalization;
using System.Text.Json;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// O mapa de I/O esperado, montado direto do <c>scenario.json</c> com <see cref="JsonDocument"/>, sem passar pelo
/// carregador de cenários do servidor: é a referência independente do critério 4 da Etapa 8. Segue o descritivo
/// (mapa Modbus) e a D27 (no <c>float32</c>, o sinal de endereço a ocupa os registradores 2a e 2a+1).
/// </summary>
internal static class ExpectedIoMap
{
    public static string[] FromScenario(string scenarioJson, string format)
    {
        using JsonDocument document = JsonDocument.Parse(scenarioJson);
        JsonElement io = document.RootElement.GetProperty("io");
        bool isFloat = format == "float32";
        int fullScale = format switch
        {
            "counts27648" => 27648,
            "counts10000" => 10000,
            "counts32767" => 32767,
            _ => 0,
        };

        var lines = new List<string>();
        foreach (JsonElement point in io.GetProperty("coils").EnumerateArray())
        {
            int a = point.GetProperty("address").GetInt32();
            lines.Add(Line("coil", a, a + 1, 1, point.GetProperty("signal").GetString()!, "digital", "write", "bool"));
        }

        foreach (JsonElement point in io.GetProperty("discreteInputs").EnumerateArray())
        {
            int a = point.GetProperty("address").GetInt32();
            lines.Add(Line("discrete-input", a, 10001 + a, 1, point.GetProperty("signal").GetString()!, "digital", "read", "bool"));
        }

        JsonElement watchdog = io.GetProperty("watchdog");
        int status = watchdog.GetProperty("statusDiscreteInput").GetInt32();
        lines.Add(Line("discrete-input", status, 10001 + status, 1, "watchdog", "watchdog-status", "read", "bool"));

        foreach (JsonElement point in io.GetProperty("holdingRegisters").EnumerateArray())
        {
            lines.Add(Analog(point, "holding-register", 40001, "write"));
        }

        int heartbeat = watchdog.GetProperty("heartbeatRegister").GetInt32();
        lines.Add(Line("holding-register", heartbeat, 40001 + heartbeat, 1, "heartbeat", "heartbeat", "write", "uint16"));

        foreach (JsonElement point in io.GetProperty("inputRegisters").EnumerateArray())
        {
            lines.Add(Analog(point, "input-register", 30001, "read"));
        }

        JsonElement diagnostics = io.GetProperty("diagnostics");
        int start = diagnostics.GetProperty("address").GetInt32();
        int k = 0;
        foreach (JsonElement value in diagnostics.GetProperty("values").EnumerateArray())
        {
            int a = start + (2 * k++);
            lines.Add(Line("input-register", a, 30001 + a, 2, value.GetProperty("signal").GetString()!, "diagnostic", "read", "float32", unit: value.GetProperty("unit").GetString()!));
        }

        return [.. lines];

        string Analog(JsonElement point, string area, int classicBase, string access)
        {
            int a = point.GetProperty("address").GetInt32();
            int register = isFloat ? 2 * a : a;
            double factor = point.TryGetProperty("unitFactor", out JsonElement f) ? f.GetDouble() : 1.0;
            return Line(
                area,
                register,
                classicBase + register,
                isFloat ? 2 : 1,
                point.GetProperty("signal").GetString()!,
                "analog",
                access,
                isFloat ? "float32" : "int16",
                isFloat ? string.Empty : "0",
                isFloat ? string.Empty : fullScale.ToString(CultureInfo.InvariantCulture),
                point.GetProperty("unit").GetString()!,
                Engineering(point.GetProperty("lrv").GetDouble() * factor),
                Engineering(point.GetProperty("urv").GetDouble() * factor));
        }
    }

    private static string Line(
        string area,
        int address,
        int classic,
        int length,
        string signal,
        string kind,
        string access,
        string dataType,
        string rawMin = "",
        string rawMax = "",
        string unit = "",
        string lrv = "",
        string urv = "") =>
        string.Join(',', area, I(address), I(classic), I(length), signal, kind, access, dataType, rawMin, rawMax, unit, lrv, urv);

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Valor em unidade de engenharia com 12 algarismos significativos (16,000000000000004 bar → 16).</summary>
    private static string Engineering(double value) =>
        double.Parse(value.ToString("G12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
}
