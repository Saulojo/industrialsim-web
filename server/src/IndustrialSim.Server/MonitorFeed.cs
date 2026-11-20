using System.Diagnostics;
using IndustrialSim.Contracts;
using IndustrialSim.IO;
using IndustrialSim.Modbus;
using IndustrialSim.Sessions;

namespace IndustrialSim.Server;

/// <summary>
/// Monta as mensagens do monitor de registradores de uma conexão (D44): as linhas do mapa no formato atual e,
/// a cada chamada, os valores brutos e de engenharia, as marcas de forçado e a última escrita em cada linha.
/// </summary>
/// <remarks>
/// As linhas só são reenviadas quando o formato ou a ordem das palavras mudam. As escritas são reenviadas quando
/// muda quem escreveu em alguma linha, ou uma vez por segundo (para atualizar o horário): um mestre que escreve a
/// cada ciclo não aumenta a banda. Usado só pela tarefa que envia da conexão.
/// </remarks>
internal sealed class MonitorFeed(PlantInstance plant)
{
    private static readonly TimeSpan WritesRefresh = TimeSpan.FromSeconds(1);

    private readonly Stopwatch _sinceWrites = new();
    private (AnalogFormat Format, WordOrder Order)? _layoutKey;
    private IReadOnlyList<IoMapEntry> _entries = [];
    private string? _writesSignature;

    /// <summary>Esquece o que já foi enviado: a próxima chamada reenvia as linhas e as escritas.</summary>
    public void Reset()
    {
        _layoutKey = null;
        _writesSignature = null;
    }

    /// <summary>As linhas (se mudaram desde o último envio) e os valores atuais.</summary>
    public (MonitorLayoutMessage? Layout, MonitorMessage Values) Next()
    {
        IoSnapshot snapshot = plant.Io.Image.Snapshot();
        MonitorLayoutMessage? layout = null;
        if (_layoutKey != (snapshot.Format, snapshot.WordOrder))
        {
            _layoutKey = (snapshot.Format, snapshot.WordOrder);
            _entries = plant.Io.Map.Describe(snapshot.Format);
            _writesSignature = null;
            layout = new MonitorLayoutMessage(
                SettingText.Of(snapshot.Format),
                SettingText.Of(snapshot.WordOrder),
                [.. _entries.Select(e => new MonitorPointInfo(
                    ContractMapping.Area(e.Area), e.Address, e.ClassicAddress, e.Length, e.Signal, e.Unit, ContractMapping.Kind(e.Kind)))]);
        }

        double[] values = plant.Io.DecodeEngineering(_entries, snapshot);
        var raw = new List<int>(_entries.Count + 8);
        var forced = new List<int>();
        var writes = new List<MonitorWriteInfo>();
        for (int k = 0; k < _entries.Count; k++)
        {
            IoMapEntry entry = _entries[k];
            for (int r = entry.Address; r < entry.Address + entry.Length; r++)
            {
                raw.Add(entry.Area switch
                {
                    ModbusArea.Coil => snapshot.Coils[r] ? 1 : 0,
                    ModbusArea.DiscreteInput => snapshot.DiscreteInputs[r] ? 1 : 0,
                    ModbusArea.HoldingRegister => snapshot.HoldingRegisters[r],
                    _ => snapshot.InputRegisters[r],
                });
            }

            bool isForced = entry.Area switch
            {
                ModbusArea.DiscreteInput => snapshot.ForcedDiscreteInputs[entry.Address],
                ModbusArea.InputRegister => snapshot.ForcedInputRegisters[entry.Address],
                _ => false,
            };
            if (isForced)
            {
                forced.Add(k);
            }

            if (entry.Area is ModbusArea.Coil or ModbusArea.HoldingRegister && LastWrite(entry) is WriteRecord write)
            {
                writes.Add(new MonitorWriteInfo(k, write.Author, write.TimeUtc));
            }
        }

        string signature = string.Join(';', writes.Select(w => $"{w.Point}:{w.Author}"));
        IReadOnlyList<MonitorWriteInfo>? sentWrites = null;
        if (signature != _writesSignature || _sinceWrites.Elapsed >= WritesRefresh)
        {
            _writesSignature = signature;
            _sinceWrites.Restart();
            sentWrites = writes;
        }

        var message = new MonitorMessage(snapshot.StepCount, raw, [.. values.Select(v => (float)v)], forced, sentWrites);
        return (layout, message);
    }

    /// <summary>A escrita mais recente nos registradores do ponto.</summary>
    private WriteRecord? LastWrite(IoMapEntry entry)
    {
        WriteRecord? latest = null;
        for (int r = entry.Address; r < entry.Address + entry.Length; r++)
        {
            if (plant.Modbus.Device.Writes.Get(entry.Area, r) is WriteRecord record && (latest is null || record.TimeUtc > latest.Value.TimeUtc))
            {
                latest = record;
            }
        }

        return latest;
    }
}
