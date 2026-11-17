using IndustrialSim.IO;

namespace IndustrialSim.Modbus;

/// <summary>Última escrita num endereço: quem escreveu (o IP do mestre) e quando.</summary>
/// <param name="Author">Autor da escrita: o IP do mestre Modbus.</param>
/// <param name="TimeUtc">Horário da escrita.</param>
public readonly record struct WriteRecord(string Author, DateTimeOffset TimeUtc);

/// <summary>Um endereço escrito, com a sua última escrita.</summary>
/// <param name="Area">Coil ou holding register.</param>
/// <param name="Address">Endereço em base 0.</param>
/// <param name="Write">Última escrita.</param>
public sealed record WriteEntry(ModbusArea Area, int Address, WriteRecord Write);

/// <summary>
/// Autor e horário da última escrita em cada coil e holding register de uma planta, para o monitor de
/// registradores (Etapa 8). Dimensionado como a imagem de I/O; thread-safe.
/// </summary>
public sealed class WriteLog
{
    private readonly Lock _gate = new();
    private readonly WriteRecord?[] _coils;
    private readonly WriteRecord?[] _holdingRegisters;

    public WriteLog(int coilCount, int holdingRegisterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coilCount);
        ArgumentOutOfRangeException.ThrowIfNegative(holdingRegisterCount);
        _coils = new WriteRecord?[coilCount];
        _holdingRegisters = new WriteRecord?[holdingRegisterCount];
    }

    /// <summary>Registra a escrita de <paramref name="count"/> endereços a partir de <paramref name="start"/>.</summary>
    public void Record(ModbusArea area, int start, int count, string author, DateTimeOffset timeUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(author);
        WriteRecord?[] records = Area(area);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, records.Length - start);

        var record = new WriteRecord(author, timeUtc);
        lock (_gate)
        {
            records.AsSpan(start, count).Fill(record);
        }
    }

    /// <summary>Última escrita no endereço; nula se ninguém escreveu nele ou se ele não existe.</summary>
    public WriteRecord? Get(ModbusArea area, int address)
    {
        WriteRecord?[] records = Area(area);
        if (address < 0 || address >= records.Length)
        {
            return null;
        }

        lock (_gate)
        {
            return records[address];
        }
    }

    /// <summary>Os endereços já escritos: coils e depois holding registers, em ordem de endereço.</summary>
    public IReadOnlyList<WriteEntry> Entries()
    {
        var entries = new List<WriteEntry>();
        lock (_gate)
        {
            Collect(ModbusArea.Coil, _coils, entries);
            Collect(ModbusArea.HoldingRegister, _holdingRegisters, entries);
        }

        return entries;
    }

    private static void Collect(ModbusArea area, WriteRecord?[] records, List<WriteEntry> entries)
    {
        for (int address = 0; address < records.Length; address++)
        {
            if (records[address] is WriteRecord record)
            {
                entries.Add(new WriteEntry(area, address, record));
            }
        }
    }

    private WriteRecord?[] Area(ModbusArea area) => area switch
    {
        ModbusArea.Coil => _coils,
        ModbusArea.HoldingRegister => _holdingRegisters,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Só coils e holding registers são escritos pelo mestre."),
    };
}
