using System.Text.Json;

namespace IndustrialSim.Core.Scenarios;

/// <summary>Parte do esquema que descreve a seção <c>hmi</c> (D39).</summary>
public static partial class ScenarioSchemaWriter
{
    private static void WriteHmiProperty(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("hmi");
        writer.WriteString("$ref", "#/$defs/hmi");
        writer.WriteString("description", "IHM web: distúrbios que o operador comanda e saídas que viram alarme.");
        writer.WriteEndObject();
    }

    private static void WriteHmiDefinitions(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("hmi");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartObject("properties");
        WriteArrayOf(writer, "disturbances", "disturbance", "Entradas externas que o operador muda pela IHM. Não podem ser comandadas pelo mapa Modbus.");
        WriteArrayOf(writer, "alarms", "alarm", "Saídas externas que, ativas (≥ 0,5), aparecem como alarme.");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("disturbance");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["input"]);
        writer.WriteStartObject("properties");
        WriteSimple(writer, "input", "string", "Nome da entrada externa.");
        WriteNumberWithDefault(writer, "min", 0, "Menor valor, na unidade da planta.");
        WriteNumberWithDefault(writer, "max", 1, "Maior valor, na unidade da planta. Maior que min.");
        WriteSimple(writer, "unit", "string", "Unidade de engenharia mostrada na IHM.");
        WriteNumberWithDefault(writer, "unitFactor", 1, "Valor de engenharia = valor do sinal × fator. Diferente de 0.");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("alarm");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        WriteStringArray(writer, "required", ["signal"]);
        writer.WriteStartObject("properties");
        WriteSimple(writer, "signal", "string", "Nome da saída externa.");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
