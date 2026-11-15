namespace IndustrialSim.IO;

/// <summary>As quatro áreas do Modbus, do ponto de vista do mestre.</summary>
public enum ModbusArea
{
    /// <summary>Saída digital: o mestre escreve.</summary>
    Coil,

    /// <summary>Entrada digital: o mestre lê.</summary>
    DiscreteInput,

    /// <summary>Entrada analógica: o mestre lê.</summary>
    InputRegister,

    /// <summary>Saída analógica: o mestre escreve.</summary>
    HoldingRegister,
}

/// <summary>Notações de endereço Modbus: base 0 (a do protocolo) e clássica, com prefixo de área.</summary>
public static class ModbusAddress
{
    /// <summary>
    /// Endereço na notação clássica: coil 0 = 1, entrada discreta 0 = 10001, input register 0 = 30001,
    /// holding register 0 = 40001. Acima de 9998 a notação passa a ter 6 dígitos (ex.: 410000), como nas
    /// ferramentas que aceitam a faixa inteira.
    /// </summary>
    public static int Classic(ModbusArea area, int address)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(address);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(address, 65535);

        int prefix = area switch
        {
            ModbusArea.Coil => 0,
            ModbusArea.DiscreteInput => 1,
            ModbusArea.InputRegister => 3,
            ModbusArea.HoldingRegister => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Área Modbus desconhecida."),
        };

        int scale = address > 9998 ? 100_000 : 10_000;
        return (prefix * scale) + address + 1;
    }
}
