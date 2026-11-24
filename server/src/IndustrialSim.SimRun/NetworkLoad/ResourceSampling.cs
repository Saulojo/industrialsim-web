using System.Globalization;

namespace IndustrialSim.SimRun.NetworkLoad;

/// <summary>
/// Uma amostra de CPU e RAM da VM, numa linha de <c>deploy/scripts/sample-resources.sh</c>. Os contadores de CPU
/// são acumulados (jiffies); o uso sai da diferença entre duas amostras.
/// </summary>
internal sealed record ResourceSample(
    double UptimeSeconds,
    int Cpus,
    int ClockTicks,
    long CpuTotal,
    long CpuIdle,
    long MemTotalKb,
    long MemAvailableKb,
    long ServiceCpu,
    long ServiceRssKb)
{
    /// <summary>Lê uma linha <c>S ...</c>; devolve falso para qualquer outra linha (avisos do ssh, por exemplo).</summary>
    public static bool TryParse(string? line, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ResourceSample? sample)
    {
        sample = null;
        string[] parts = (line ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 10 || parts[0] != "S")
        {
            return false;
        }

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double uptime)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cpus)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clock)
            || !TryLong(parts[4], out long total)
            || !TryLong(parts[5], out long idle)
            || !TryLong(parts[6], out long memTotal)
            || !TryLong(parts[7], out long memAvailable)
            || !TryLong(parts[8], out long serviceCpu)
            || !TryLong(parts[9], out long serviceRss)
            || cpus < 1
            || clock < 1)
        {
            return false;
        }

        sample = new ResourceSample(uptime, cpus, clock, total, idle, memTotal, memAvailable, serviceCpu, serviceRss);
        return true;
    }

    private static bool TryLong(string text, out long value) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}

/// <summary>Resumo do uso de CPU e RAM da VM durante um patamar do teste de carga.</summary>
/// <param name="Samples">Amostras recebidas.</param>
/// <param name="VmCpus">Núcleos virtuais da VM.</param>
/// <param name="VmCpuMean">CPU da VM inteira, média, como fração de todos os núcleos (0 a 1).</param>
/// <param name="VmCpuMax">CPU da VM inteira, maior intervalo.</param>
/// <param name="ServiceCoresMean">CPU do serviço, média, em núcleos (1 = um núcleo inteiro).</param>
/// <param name="ServiceCoresMax">CPU do serviço, maior intervalo, em núcleos.</param>
/// <param name="MemTotalMb">RAM total da VM.</param>
/// <param name="MemUsedMaxMb">Maior RAM em uso na VM (total − disponível).</param>
/// <param name="MemAvailableMinFraction">Menor fração de RAM disponível.</param>
/// <param name="ServiceRssMaxMb">Maior memória residente do serviço.</param>
internal sealed record ResourceSummary(
    int Samples,
    int VmCpus,
    double VmCpuMean,
    double VmCpuMax,
    double ServiceCoresMean,
    double ServiceCoresMax,
    double MemTotalMb,
    double MemUsedMaxMb,
    double MemAvailableMinFraction,
    double ServiceRssMaxMb);

/// <summary>Acumula amostras e calcula o uso entre amostras consecutivas. Thread-safe.</summary>
internal sealed class ResourceStats
{
    private readonly Lock _gate = new();
    private ResourceSample? _previous;
    private int _samples;
    private int _intervals;
    private int _serviceIntervals;
    private int _cpus;
    private double _vmCpuSum;
    private double _vmCpuMax;
    private double _serviceCoresSum;
    private double _serviceCoresMax;
    private double _memTotalMb;
    private double _memUsedMaxMb;
    private double _memAvailableMin = 1.0;
    private double _rssMaxMb;

    public void Add(ResourceSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            _samples++;
            _cpus = sample.Cpus;
            _memTotalMb = sample.MemTotalKb / 1024.0;
            if (sample.MemTotalKb > 0)
            {
                _memUsedMaxMb = Math.Max(_memUsedMaxMb, (sample.MemTotalKb - sample.MemAvailableKb) / 1024.0);
                _memAvailableMin = Math.Min(_memAvailableMin, (double)sample.MemAvailableKb / sample.MemTotalKb);
            }

            if (sample.ServiceRssKb >= 0)
            {
                _rssMaxMb = Math.Max(_rssMaxMb, sample.ServiceRssKb / 1024.0);
            }

            if (_previous is { } previous)
            {
                long total = sample.CpuTotal - previous.CpuTotal;
                long idle = sample.CpuIdle - previous.CpuIdle;
                if (total > 0)
                {
                    double vmCpu = Math.Clamp(1.0 - ((double)idle / total), 0.0, 1.0);
                    _vmCpuSum += vmCpu;
                    _vmCpuMax = Math.Max(_vmCpuMax, vmCpu);
                    _intervals++;
                }

                double seconds = sample.UptimeSeconds - previous.UptimeSeconds;

                // O serviço pode ter reiniciado entre as amostras (contador menor): o intervalo fica de fora.
                if (seconds > 0 && previous.ServiceCpu >= 0 && sample.ServiceCpu >= previous.ServiceCpu)
                {
                    double cores = (sample.ServiceCpu - previous.ServiceCpu) / (double)sample.ClockTicks / seconds;
                    _serviceCoresSum += cores;
                    _serviceCoresMax = Math.Max(_serviceCoresMax, cores);
                    _serviceIntervals++;
                }
            }

            _previous = sample;
        }
    }

    public ResourceSummary? Summarize()
    {
        lock (_gate)
        {
            if (_samples == 0)
            {
                return null;
            }

            return new ResourceSummary(
                _samples,
                _cpus,
                _intervals > 0 ? _vmCpuSum / _intervals : 0.0,
                _vmCpuMax,
                _serviceIntervals > 0 ? _serviceCoresSum / _serviceIntervals : 0.0,
                _serviceCoresMax,
                _memTotalMb,
                _memUsedMaxMb,
                _memAvailableMin,
                _rssMaxMb);
        }
    }
}
