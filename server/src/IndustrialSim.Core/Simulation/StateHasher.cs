using System.Buffers.Binary;
using System.Globalization;

namespace IndustrialSim.Core.Simulation;

/// <summary>
/// Hash do estado para os testes de determinismo (RNF02): FNV-1a de 64 bits sobre os bits exatos dos valores,
/// em ordem little-endian fixa, igual em qualquer plataforma.
/// Fonte: Fowler, G.; Noll, L. C.; Vo, K.-P.; Eastlake, D. "The FNV Non-Cryptographic Hash Algorithm" (IETF).
/// </summary>
/// <remarks>
/// Compara bits, não valores: +0,0 e −0,0 geram hashes diferentes, e é isso que "idêntico bit a bit" pede.
/// Não é hash criptográfico; serve para detectar divergência, não para segurança.
/// </remarks>
public struct StateHasher
{
    private const ulong OffsetBasis = 0xCBF29CE484222325UL;
    private const ulong Prime = 0x00000100000001B3UL;

    private ulong _hash;
    private bool _started;

    /// <summary>Hash acumulado até aqui.</summary>
    public readonly ulong Value => _started ? _hash : OffsetBasis;

    public void Add(ReadOnlySpan<byte> bytes)
    {
        ulong hash = Value;
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= Prime;
        }

        _hash = hash;
        _started = true;
    }

    public void Add(ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        Add(buffer);
    }

    public void Add(long value) => Add(unchecked((ulong)value));

    public void Add(double value) => Add(BitConverter.DoubleToUInt64Bits(value));

    public void Add(bool value) => Add(value ? 1UL : 0UL);

    public void Add(ReadOnlySpan<double> values)
    {
        foreach (double value in values)
        {
            Add(value);
        }
    }

    /// <summary>Hash em 16 dígitos hexadecimais, para relatórios e evidências.</summary>
    public readonly string ToHex() => Value.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>Hash de um vetor de estado.</summary>
    public static ulong Hash(ReadOnlySpan<double> values)
    {
        var hasher = default(StateHasher);
        hasher.Add(values);
        return hasher.Value;
    }
}
