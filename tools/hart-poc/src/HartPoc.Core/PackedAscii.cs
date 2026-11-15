using System.Text;

namespace HartPoc;

/// <summary>
/// Texto "Packed" do HART: caracteres de 6 bits, quatro caracteres em três bytes,
/// completado com espaços (HCF_SPEC-127 r7.1, seção 5, "Packed").
/// </summary>
public static class PackedAscii
{
    /// <summary>Codifica <paramref name="text"/> em exatamente <paramref name="characters"/> caracteres (múltiplo de 4).</summary>
    public static byte[] Encode(string text, int characters)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (characters <= 0 || characters % 4 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characters), "O tamanho precisa ser múltiplo de 4.");
        }

        var padded = text.ToUpperInvariant().PadRight(characters)[..characters];
        var packed = new byte[characters / 4 * 3];
        for (int i = 0, o = 0; i < characters; i += 4, o += 3)
        {
            var bits = (Code(padded[i]) << 18) | (Code(padded[i + 1]) << 12) | (Code(padded[i + 2]) << 6) | Code(padded[i + 3]);
            packed[o] = (byte)(bits >> 16);
            packed[o + 1] = (byte)(bits >> 8);
            packed[o + 2] = (byte)bits;
        }

        return packed;
    }

    /// <summary>Decodifica sem cortar os espaços do fim; o tamanho precisa ser múltiplo de 3 bytes.</summary>
    public static string Decode(ReadOnlySpan<byte> packed)
    {
        if (packed.Length % 3 != 0)
        {
            throw new ArgumentException("O tamanho precisa ser múltiplo de 3 bytes.", nameof(packed));
        }

        var text = new StringBuilder(packed.Length / 3 * 4);
        for (var i = 0; i < packed.Length; i += 3)
        {
            var bits = (packed[i] << 16) | (packed[i + 1] << 8) | packed[i + 2];
            text.Append(Character(bits >> 18));
            text.Append(Character(bits >> 12));
            text.Append(Character(bits >> 6));
            text.Append(Character(bits));
        }

        return text.ToString();
    }

    // Só o intervalo 0x20–0x5F existe no alfabeto de 6 bits; o resto vira '?'.
    private static int Code(char c) => (c is >= ' ' and <= '_' ? c : '?') & 0x3F;

    // Códigos 0x00–0x1F são '@'..'_' (bit 6 reposto); 0x20–0x3F são ' '..'?'.
    private static char Character(int code)
    {
        code &= 0x3F;
        return (char)(code < 0x20 ? code + 0x40 : code);
    }
}
