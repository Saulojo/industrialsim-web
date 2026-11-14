using System.Numerics;

namespace IndustrialSim.Core.Numerics;

/// <summary>
/// Gerador pseudoaleatório com semente, para ruído reprodutível (D06, RNF02).
/// Algoritmo xoshiro256** 1.0, com o estado inicial preenchido por SplitMix64 a partir da semente.
/// Fonte: Blackman, D.; Vigna, S. "Scrambled linear pseudorandom number generators",
/// ACM Trans. Math. Softw., v. 47, n. 4, 2021; código de referência em domínio público (prng.di.unimi.it).
/// </summary>
/// <remarks>
/// Implementação própria porque a sequência de <see cref="System.Random"/> com semente não é
/// garantida entre versões do .NET; aqui a sequência é parte do contrato e tem teste com vetores fixos.
/// </remarks>
public sealed class SeededRandom
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public SeededRandom(ulong seed)
    {
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    private SeededRandom(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    /// <summary>Recria um gerador a partir de um estado salvo por <see cref="GetState"/>.</summary>
    public static SeededRandom FromState(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0)
        {
            throw new ArgumentException("O estado do xoshiro256** não pode ser todo zero.");
        }

        return new SeededRandom(s0, s1, s2, s3);
    }

    /// <summary>Estado interno, para salvar e retomar a sequência do mesmo ponto.</summary>
    public (ulong S0, ulong S1, ulong S2, ulong S3) GetState() => (_s0, _s1, _s2, _s3);

    /// <summary>Próximo inteiro de 64 bits da sequência.</summary>
    public ulong NextUInt64()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);

        return result;
    }

    /// <summary>Uniforme em [0, 1), com 53 bits de resolução.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>
    /// Normal padrão (média 0, desvio 1) pela transformada de Box–Muller
    /// (Box, G. E. P.; Muller, M. E., Ann. Math. Statist., v. 29, n. 2, 1958).
    /// Consome sempre dois uniformes; o segundo valor do par é descartado para o estado ser só o do gerador.
    /// </summary>
    public double NextGaussian()
    {
        double u1 = 1.0 - NextDouble(); // (0, 1]: evita log(0)
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Normal com a média e o desvio padrão dados.</summary>
    public double NextGaussian(double mean, double standardDeviation) => mean + (standardDeviation * NextGaussian());

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
