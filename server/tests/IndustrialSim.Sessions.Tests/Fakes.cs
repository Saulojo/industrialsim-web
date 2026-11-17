using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Sessions.Tests;

/// <summary>Relógio controlado pelo teste: o tempo só anda por <see cref="Advance"/>. Frequência de 10 MHz.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private long _timestamp;
    private DateTimeOffset _utcNow = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _timestamp;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public void Advance(TimeSpan duration)
    {
        lock (_gate)
        {
            _timestamp += duration.Ticks;
            _utcNow += duration;
        }
    }
}

/// <summary>Espera que só avança o relógio falso, exatamente pela duração pedida.</summary>
internal sealed class ManualWaiter(ManualTimeProvider time) : IPreciseWaiter
{
    public string Kind => "manual";

    public int Waits { get; private set; }

    public void Wait(TimeSpan duration)
    {
        Waits++;
        if (duration > TimeSpan.Zero)
        {
            time.Advance(duration);
        }
    }

    public void Dispose()
    {
    }
}

/// <summary>Planta de teste: conta passos e pode demorar (no relógio falso) ou falhar num passo escolhido.</summary>
internal sealed class CountingStepper(string name = "teste") : IRealTimeStepper
{
    private long _steps;
    private int _faults;

    public string Name => name;

    public long Steps => Interlocked.Read(ref _steps);

    public int Faults => Volatile.Read(ref _faults);

    /// <summary>Executado dentro do passo, com o número do passo (a partir de 0).</summary>
    public Action<long>? OnStep { get; set; }

    public void Step()
    {
        long step = Interlocked.Increment(ref _steps) - 1;
        OnStep?.Invoke(step);
    }

    public void OnFault(Exception exception) => Interlocked.Increment(ref _faults);
}
