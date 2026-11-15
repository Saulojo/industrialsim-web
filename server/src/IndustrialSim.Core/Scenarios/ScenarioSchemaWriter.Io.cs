using System.Text.Json;

namespace IndustrialSim.Core.Scenarios;

/// <summary>Parte do esquema que descreve a seção <c>io</c> (mapa Modbus).</summary>
public static partial class ScenarioSchemaWriter
{
    private static void WriteIoProperty(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("io");
        writer.WriteString("$ref", "#/$defs/io");
        writer.WriteString("description", "Mapa Modbus da planta: endereços (base 0) ligados às entradas e saídas externas.");
        writer.WriteEndObject();
    }

    private static void WriteIoDefinitions(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("io");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartObject("properties");
        WriteArrayOf(writer, "coils", "digitalPoint", "O mestre escreve: liga uma entrada externa (0 ou 1).");
        WriteArrayOf(writer, "discreteInputs", "digitalPoint", "O mestre lê: uma saída externa (≥ 0,5 = 1).");
        WriteArrayOf(writer, "holdingRegisters", "analogPoint", "O mestre escreve: comando 4–20 mA de uma entrada externa.");
        WriteArrayOf(writer, "inputRegisters", "analogPoint", "O mestre lê: medição 4–20 mA de uma saída externa.");
        writer.WriteStartObject("diagnostics");
        writer.WriteString("$ref", "#/$defs/diagnostics");
        writer.WriteEndObject();
        writer.WriteStartObject("watchdog");
        writer.WriteString("$ref", "#/$defs/watchdog");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("digitalPoint");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["address", "signal"]);
        writer.WriteStartObject("properties");
        WriteAddress(writer, "address", ScenarioLoader.MaxAddress, "Endereço Modbus, em base 0.");
        WriteSimple(writer, "signal", "string", "Nome da entrada externa (coil) ou da saída externa (entrada discreta).");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("analogPoint");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["address", "signal"]);
        writer.WriteStartObject("properties");
        WriteAddress(
            writer,
            "address",
            ScenarioLoader.MaxAnalogAddress,
            "Endereço Modbus, em base 0. No formato float32, o sinal de endereço a ocupa 2a e 2a+1.");
        WriteSimple(writer, "signal", "string", "Nome da entrada externa (holding register) ou da saída externa (input register).");
        WriteNumberWithDefault(writer, "lrv", 0, "Valor do sinal, na unidade da planta, em 4 mA.");
        WriteNumberWithDefault(writer, "urv", 1, "Valor do sinal, na unidade da planta, em 20 mA. Diferente de lrv.");
        WriteSimple(writer, "unit", "string", "Unidade de engenharia, usada no formato float32 e na interface.");
        WriteNumberWithDefault(writer, "unitFactor", 1, "Valor de engenharia = valor do sinal × fator (ex.: 1e-5 de Pa para bar). Diferente de 0.");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("diagnostics");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteString("description", "Bloco de diagnóstico nos input registers: um float32 (2 registradores) por valor.");
        WriteStringArray(writer, "required", ["address", "values"]);
        writer.WriteStartObject("properties");
        WriteAddress(writer, "address", ScenarioLoader.MaxAddress, "Endereço do primeiro registrador do bloco, em base 0.");
        writer.WriteStartObject("values");
        writer.WriteString("type", "array");
        writer.WriteNumber("minItems", 1);
        writer.WriteStartObject("items");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["signal"]);
        writer.WriteStartObject("properties");
        WriteSimple(writer, "signal", "string", "Nome da saída externa.");
        WriteSimple(writer, "unit", "string", "Unidade de engenharia.");
        WriteNumberWithDefault(writer, "unitFactor", 1, "Valor de engenharia = valor do sinal × fator. Diferente de 0.");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("watchdog");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteString("description", "Se o heartbeat ficar o tempo limite sem mudar, a planta vai ao estado seguro. Desligado por padrão.");
        WriteStringArray(writer, "required", ["heartbeatRegister", "safeState"]);
        writer.WriteStartObject("properties");
        WriteAddress(writer, "heartbeatRegister", ScenarioLoader.MaxAddress, "Holding register do heartbeat: contador livre, basta mudar.");
        WriteAddress(writer, "statusDiscreteInput", ScenarioLoader.MaxAddress, "Entrada discreta que vai a 1 quando o watchdog dispara.");
        writer.WriteStartObject("timeoutSeconds");
        writer.WriteString("type", "number");
        writer.WriteNumber("exclusiveMinimum", 0);
        writer.WriteNumber("default", ScenarioLoader.DefaultWatchdogTimeoutSeconds);
        writer.WriteString("description", "Tempo limite padrão, em segundos.");
        writer.WriteEndObject();
        writer.WriteStartObject("safeState");
        writer.WriteString("type", "array");
        writer.WriteStartObject("items");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["input", "value"]);
        writer.WriteStartObject("properties");
        WriteSimple(writer, "input", "string", "Nome da entrada externa.");
        WriteSimple(writer, "value", "number", "Valor seguro, na unidade da planta.");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteString("description", "Entradas externas e os valores que recebem no estado seguro; as demais ficam como estavam.");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteAddress(Utf8JsonWriter writer, string name, int maximum, string description)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "integer");
        writer.WriteNumber("minimum", 0);
        writer.WriteNumber("maximum", maximum);
        writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteNumberWithDefault(Utf8JsonWriter writer, string name, double defaultValue, string description)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "number");
        writer.WriteNumber("default", defaultValue);
        writer.WriteString("description", description);
        writer.WriteEndObject();
    }
}
