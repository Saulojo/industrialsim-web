namespace IndustrialSim.Sessions.RealTime;

/// <summary>Resumo de uma série de durações, em milissegundos.</summary>
/// <param name="Count">Número de amostras.</param>
/// <param name="MeanMs">Média.</param>
/// <param name="P50Ms">Mediana (limite superior da classe do histograma).</param>
/// <param name="P99Ms">Percentil 99 (limite superior da classe do histograma).</param>
/// <param name="MaxMs">Maior valor medido.</param>
public sealed record DurationSummary(long Count, double MeanMs, double P50Ms, double P99Ms, double MaxMs)
{
    public static DurationSummary Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Histograma de durações com classes de 10 µs até 100 ms; acima disso, a amostra só entra na contagem e no
/// máximo. Os percentis são o limite superior da classe (sem passar do máximo medido), então nunca ficam abaixo
/// do valor real. Gravado pela thread do laço e lido por outras, sob trava.
/// </summary>
internal sealed class DurationHistogram
{
    public const double BucketMicroseconds = 10.0;
    private const int BucketCount = 10_000;

    private readonly Lock _gate = new();
    private readonly long[] _buckets = new long[BucketCount + 1];
    private long _count;
    private double _sumMicroseconds;
    private double _maxMicroseconds;

    public void Record(double microseconds)
    {
        double value = Math.Max(0.0, microseconds);
        int bucket = (int)Math.Min(BucketCount, Math.Floor(value / BucketMicroseconds));
        lock (_gate)
        {
            _buckets[bucket]++;
            _count++;
            _sumMicroseconds += value;
            _maxMicroseconds = Math.Max(_maxMicroseconds, value);
        }
    }

    /// <summary>Soma este histograma em <paramref name="target"/>.</summary>
    public void AddTo(DurationHistogram target)
    {
        lock (_gate)
        {
            lock (target._gate)
            {
                for (int i = 0; i < _buckets.Length; i++)
                {
                    target._buckets[i] += _buckets[i];
                }

                target._count += _count;
                target._sumMicroseconds += _sumMicroseconds;
                target._maxMicroseconds = Math.Max(target._maxMicroseconds, _maxMicroseconds);
            }
        }
    }

    public DurationSummary Summarize()
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                return DurationSummary.Empty;
            }

            return new DurationSummary(
                _count,
                _sumMicroseconds / _count / 1000.0,
                Percentile(0.50) / 1000.0,
                Percentile(0.99) / 1000.0,
                _maxMicroseconds / 1000.0);
        }
    }

    private double Percentile(double fraction)
    {
        long rank = Math.Max(1, (long)Math.Ceiling(fraction * _count));
        long cumulative = 0;
        for (int i = 0; i < BucketCount; i++)
        {
            cumulative += _buckets[i];
            if (cumulative >= rank)
            {
                return Math.Min((i + 1) * BucketMicroseconds, _maxMicroseconds);
            }
        }

        return _maxMicroseconds;
    }
}
