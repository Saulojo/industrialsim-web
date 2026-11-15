namespace IndustrialSim.IO;

/// <summary>
/// Conversão entre corrente (mA) e contagens nos formatos inteiros, e entre <c>float32</c> e dois registradores.
/// Funções puras, sem estado.
/// </summary>
/// <remarks>
/// Formato 0–27648: mesma representação do cartão analógico Siemens para 4–20 mA (SIMATIC S7-1500/ET 200MP,
/// "Analog value processing", tabela de representação de valores analógicos em faixas de corrente): faixa nominal
/// 0 a 27648, sobrefaixa até 32511 e subfaixa até −4864. Assim 3,6 mA = −691 e 21 mA = 29376.
/// Nos formatos 0–10000 e 0–32767 o valor é limitado ao que cabe num inteiro de 16 bits com sinal (D28).
/// </remarks>
public static class AnalogCodec
{
    /// <summary>Contagens em 20 mA. Só para os formatos inteiros.</summary>
    public static int FullScale(AnalogFormat format) => format switch
    {
        AnalogFormat.Counts27648 => 27648,
        AnalogFormat.Counts10000 => 10000,
        AnalogFormat.Counts32767 => 32767,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Só os formatos inteiros têm fim de escala em contagens."),
    };

    /// <summary>Menor contagem representável no formato.</summary>
    public static int MinCounts(AnalogFormat format) => format == AnalogFormat.Counts27648 ? -4864 : short.MinValue;

    /// <summary>Maior contagem representável no formato.</summary>
    public static int MaxCounts(AnalogFormat format) => format == AnalogFormat.Counts27648 ? 32511 : short.MaxValue;

    /// <summary>Corrente → contagens, arredondando à contagem mais próxima e limitando à faixa do formato.</summary>
    public static short MilliampsToCounts(double milliamps, AnalogFormat format)
    {
        int fullScale = FullScale(format);
        if (double.IsNaN(milliamps))
        {
            return 0;
        }

        double counts = Math.Round(
            (milliamps - CurrentLoop.ZeroMilliamps) / CurrentLoop.SpanMilliamps * fullScale,
            MidpointRounding.AwayFromZero);
        return (short)Math.Clamp(counts, MinCounts(format), MaxCounts(format));
    }

    /// <summary>Contagens → corrente.</summary>
    public static double CountsToMilliamps(short counts, AnalogFormat format) =>
        CurrentLoop.ZeroMilliamps + (CurrentLoop.SpanMilliamps * counts / FullScale(format));

    /// <summary>O registrador (16 bits sem sinal no protocolo) que carrega as contagens em complemento de dois.</summary>
    public static ushort ToRegister(short counts) => unchecked((ushort)counts);

    /// <summary>As contagens lidas do registrador como <c>INT</c>.</summary>
    public static short FromRegister(ushort register) => unchecked((short)register);

    /// <summary>Grava um <c>float32</c> IEEE 754 em dois registradores, na ordem de palavras pedida.</summary>
    public static void WriteFloat32(float value, WordOrder order, Span<ushort> registers)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        ushort high = (ushort)(bits >> 16);
        ushort low = (ushort)(bits & 0xFFFF);
        if (order == WordOrder.HighWordFirst)
        {
            registers[0] = high;
            registers[1] = low;
        }
        else
        {
            registers[0] = low;
            registers[1] = high;
        }
    }

    /// <summary>Lê um <c>float32</c> IEEE 754 de dois registradores, na ordem de palavras pedida.</summary>
    public static float ReadFloat32(ReadOnlySpan<ushort> registers, WordOrder order)
    {
        (ushort high, ushort low) = order == WordOrder.HighWordFirst ? (registers[0], registers[1]) : (registers[1], registers[0]);
        return BitConverter.UInt32BitsToSingle(((uint)high << 16) | low);
    }
}
