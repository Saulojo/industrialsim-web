using System.Globalization;
using System.Text;
using IndustrialSim.Contracts;
using IndustrialSim.IO;
using IndustrialSim.Sessions;

namespace IndustrialSim.Server;

/// <summary>
/// Exportação do mapa de I/O de uma planta (Etapa 8), pronto para configurar o mestre: no formato analógico e
/// com as faixas atuais (no <c>float32</c> os endereços mudam, D27; o HART pode mudar as faixas). Em JSON
/// (<c>GET /api/plants/{id}/io-map</c>, usado pela página de impressão em PDF) e em CSV
/// (<c>GET /api/plants/{id}/io-map.csv</c>).
/// </summary>
internal static class IoMapApi
{
    /// <summary>Colunas do CSV, na ordem de <see cref="IoMapRow"/>.</summary>
    public const string CsvHeader = "area,address,classicAddress,length,signal,kind,access,dataType,rawMin,rawMax,unit,lrv,urv";

    public static void MapIoMapApi(this RouteGroupBuilder plants)
    {
        plants.MapGet("/{id:guid}/io-map", GetJson);
        plants.MapGet("/{id:guid}/io-map.csv", GetCsv);
    }

    private static IResult GetJson(Guid id, InstanceManager manager)
    {
        if (!manager.TryGet(id, out PlantInstance? plant))
        {
            return SimulationApi.PlantNotFound();
        }

        plant.Touch();
        return Results.Ok(Describe(plant));
    }

    private static IResult GetCsv(Guid id, InstanceManager manager)
    {
        if (!manager.TryGet(id, out PlantInstance? plant))
        {
            return SimulationApi.PlantNotFound();
        }

        plant.Touch();
        IoMapResponse map = Describe(plant);
        return Results.File(Encoding.UTF8.GetBytes(ToCsv(map)), "text/csv; charset=utf-8", $"{map.ScenarioId}-io-map-{map.Format}.csv");
    }

    /// <summary>O mapa da planta no formato e com as faixas pedidos agora.</summary>
    internal static IoMapResponse Describe(PlantInstance plant)
    {
        PlantSettings settings = plant.Settings;
        AnalogFormat format = settings.Io.Format;
        IoMap map = plant.Io.Map;
        var rows = new List<IoMapRow>();
        foreach (IoMapEntry entry in map.Describe(format))
        {
            bool analog = entry.Kind == IoPointKind.Analog;
            (double Lrv, double Urv, double Factor)? range = null;
            if (analog)
            {
                AnalogPoint point = entry.Area == ModbusArea.HoldingRegister ? map.HoldingRegisters[entry.Index] : map.InputRegisters[entry.Index];
                (double lrv, double urv) = entry.Area == ModbusArea.HoldingRegister ? (point.Lrv, point.Urv) : plant.Io.GetRange(point.Address);
                range = (lrv, urv, point.UnitFactor);
            }

            bool counts = analog && format != AnalogFormat.Float32;
            rows.Add(new IoMapRow(
                ContractMapping.Area(entry.Area),
                entry.Address,
                entry.ClassicAddress,
                entry.Length,
                entry.Signal,
                ContractMapping.Kind(entry.Kind),
                entry.Area is ModbusArea.Coil or ModbusArea.HoldingRegister ? "write" : "read",
                DataType(entry.Kind, format),
                counts ? 0 : null,
                counts ? AnalogCodec.FullScale(format) : null,
                entry.Unit,
                range is var (l, _, f) ? Clean(l * f) : null,
                range is var (_, u, g) ? Clean(u * g) : null));
        }

        return new IoMapResponse(
            plant.Id,
            plant.ScenarioId,
            SettingText.Of(format),
            SettingText.Of(settings.Io.WordOrder),
            settings.UnitId,
            plant.Modbus.Port,
            rows);
    }

    /// <summary>CSV com cabeçalho, separado por vírgulas, números na cultura invariante.</summary>
    internal static string ToCsv(IoMapResponse map)
    {
        var text = new StringBuilder(CsvHeader).Append('\n');
        foreach (IoMapRow row in map.Rows)
        {
            text.AppendJoin(',',
                row.Area,
                Number(row.Address),
                Number(row.ClassicAddress),
                Number(row.Length),
                Quote(row.Signal),
                row.Kind,
                row.Access,
                row.DataType,
                row.RawMin is int min ? Number(min) : string.Empty,
                row.RawMax is int max ? Number(max) : string.Empty,
                Quote(row.Unit),
                row.Lrv is double lrv ? lrv.ToString("R", CultureInfo.InvariantCulture) : string.Empty,
                row.Urv is double urv ? urv.ToString("R", CultureInfo.InvariantCulture) : string.Empty);
            text.Append('\n');
        }

        return text.ToString();
    }

    private static string DataType(IoPointKind kind, AnalogFormat format) => kind switch
    {
        IoPointKind.Digital or IoPointKind.WatchdogStatus => "bool",
        IoPointKind.Heartbeat => "uint16",
        IoPointKind.Diagnostic => "float32",
        _ => format == AnalogFormat.Float32 ? "float32" : "int16",
    };

    /// <summary>Tira o resto da conversão de unidade (ex.: 1,6e6 Pa × 1e-5 = 16,000000000000004 bar → 16).</summary>
    private static double Clean(double value) => double.Parse(value.ToString("G12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Quote(string value) =>
        value.AsSpan().IndexOfAny(",\"\n\r") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
