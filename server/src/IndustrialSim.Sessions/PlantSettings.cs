using System.Globalization;
using System.Text.Json;
using IndustrialSim.IO;
using IndustrialSim.Sessions.Modbus;

namespace IndustrialSim.Sessions;

/// <summary>
/// Configurações de comunicação de uma planta (descritivo, "Configurações e diagnóstico da planta"): as de I/O,
/// o Unit ID e quantos mestres podem ser autorizados. Valem enquanto a planta existir (a persistência vem com as
/// contas, na Etapa 10).
/// </summary>
/// <param name="Io">Formato analógico, ordem das palavras, watchdog e bloco de diagnóstico.</param>
/// <param name="UnitId">Unit ID a que a planta responde (1 a 255).</param>
/// <param name="MasterPolicy">Um mestre por vez ou vários.</param>
public sealed record PlantSettings(IoSettings Io, byte UnitId, MasterPolicy MasterPolicy);

/// <summary>Mudança de configurações: só os campos não nulos mudam.</summary>
public sealed record PlantSettingsUpdate(
    AnalogFormat? Format = null,
    WordOrder? WordOrder = null,
    bool? WatchdogEnabled = null,
    double? WatchdogTimeoutSeconds = null,
    bool? DiagnosticsEnabled = null,
    byte? UnitId = null,
    MasterPolicy? MasterPolicy = null);

/// <summary>Resultado de <see cref="PlantInstance.UpdateSettings"/>.</summary>
public enum SettingsUpdateResult
{
    /// <summary>Aplicadas; valem a partir do passo seguinte (o Unit ID, na próxima requisição).</summary>
    Applied,

    /// <summary>Algum valor fora do permitido; nada mudou.</summary>
    Invalid,

    /// <summary>"Um mestre por vez" pedido com mais de um IP autorizado; nada mudou (D42).</summary>
    SeveralMastersAuthorized,

    /// <summary>A planta não está rodando nem pausada.</summary>
    PlantNotRunning,
}

/// <summary>
/// Texto estável dos valores de configuração no log de comunicação e no contrato: o nome do valor em camelCase
/// (<c>counts27648</c>, <c>float32</c>, <c>highWordFirst</c>, <c>single</c>…), números na cultura invariante.
/// </summary>
public static class SettingText
{
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    public static string Of(bool value) => value ? "true" : "false";

    public static string Of(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static string Of(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Valor do enum a partir do texto de <see cref="Of{TEnum}(TEnum)"/>; falso se não houver.</summary>
    public static bool TryParse<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(Of(candidate), text, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
