using IndustrialSim.Contracts;
using IndustrialSim.IO;
using IndustrialSim.Modbus;
using IndustrialSim.Sessions;
using IndustrialSim.Sessions.Modbus;

namespace IndustrialSim.Server;

/// <summary>Conversões entre os tipos das plantas em execução e o contrato cliente–servidor.</summary>
internal static class ContractMapping
{
    public static string Area(ModbusArea area) => SettingText.OfArea(area);

    public static string Kind(IoPointKind kind) => kind switch
    {
        IoPointKind.Digital => MonitorPointKinds.Digital,
        IoPointKind.Diagnostic => MonitorPointKinds.Diagnostic,
        IoPointKind.Heartbeat => MonitorPointKinds.Heartbeat,
        IoPointKind.WatchdogStatus => MonitorPointKinds.WatchdogStatus,
        _ => MonitorPointKinds.Analog,
    };

    public static PlantSettingsInfo Settings(PlantSettings settings) => new(
        SettingText.Of(settings.Io.Format),
        SettingText.Of(settings.Io.WordOrder),
        settings.Io.WatchdogEnabled,
        settings.Io.WatchdogTimeoutSeconds,
        settings.Io.DiagnosticsEnabled,
        settings.UnitId,
        SettingText.Of(settings.MasterPolicy));

    /// <summary>Converte o comando; nulo se algum texto ou número não for válido.</summary>
    public static PlantSettingsUpdate? Update(UpdateSettingsCommand command)
    {
        AnalogFormat? format = null;
        if (command.Format is not null)
        {
            if (!SettingText.TryParse(command.Format, out AnalogFormat parsed))
            {
                return null;
            }

            format = parsed;
        }

        WordOrder? order = null;
        if (command.WordOrder is not null)
        {
            if (!SettingText.TryParse(command.WordOrder, out WordOrder parsed))
            {
                return null;
            }

            order = parsed;
        }

        MasterPolicy? policy = null;
        if (command.MasterPolicy is not null)
        {
            if (!SettingText.TryParse(command.MasterPolicy, out MasterPolicy parsed))
            {
                return null;
            }

            policy = parsed;
        }

        if (command.UnitId is < 1 or > 255)
        {
            return null;
        }

        return new PlantSettingsUpdate(
            format,
            order,
            command.WatchdogEnabled,
            command.WatchdogTimeoutSeconds,
            command.DiagnosticsEnabled,
            (byte?)command.UnitId,
            policy);
    }

    public static ForcedMessage Forced(IReadOnlyList<ForcedInput> forced) =>
        new([.. forced.Select(f => new ForcedInputInfo(Area(f.Area), f.Signal, f.Value))]);

    public static LogEntryInfo LogEntry(PlantEvent entry) => new(
        entry.Id,
        entry.Sequence,
        entry.Kind,
        entry.FirstUtc,
        entry.TimeUtc,
        entry.Count,
        [.. entry.Arguments.Select(a => new LogArgumentInfo(a.Name, a.Value))]);

    public static ModbusStatusMessage ModbusStatus(PlantModbus modbus)
    {
        (long requests, long exceptions) = modbus.SampledCounters;
        return new ModbusStatusMessage(
            modbus.Port,
            modbus.UnitId,
            [.. modbus.Pairing.Authorized.Select(a => a.ToString())],
            [.. modbus.Pairing.Pending.Select(p => new PairingRequestResponse(p.Id, p.Address.ToString(), p.RequestedUtc, p.ExpiresUtc, p.WaitingConnections))],
            [.. modbus.Connections.Select(c => new MasterConnectionInfo(
                c.Id,
                ModbusTcpServer.Normalize(c.Remote.Address).ToString(),
                c.Remote.Port,
                c.ConnectedUtc,
                c.Authorized))],
            Math.Round(modbus.RequestsPerSecond, 1),
            requests,
            exceptions);
    }
}
