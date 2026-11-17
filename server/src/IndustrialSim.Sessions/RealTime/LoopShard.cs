using Microsoft.Extensions.Logging;

namespace IndustrialSim.Sessions.RealTime;

/// <summary>
/// Uma thread do laço de tempo real e as plantas que ela avança.
/// </summary>
/// <remarks>
/// Os ciclos seguem uma grade fixa no relógio monotônico: o ciclo k está previsto para <c>início + k × dt</c>,
/// então o erro de uma espera não se acumula (sem deriva). A cada volta, a thread espera até o próximo ciclo
/// previsto ou, se ele já passou, executa-o na hora: cada planta da lista avança um passo, na ordem em que
/// entrou. Depois de um atraso, os ciclos atrasados são executados em seguida, sem espera (recuperação), até
/// <c>maxCatchUp</c> ciclos; o que passar disso é descartado, e as plantas ficam atrás do relógio.
/// <para>
/// Plantas entram e saem só no começo de uma volta, pela própria thread; uma planta que lança exceção sai da
/// lista e é avisada, e as outras continuam.
/// </para>
/// </remarks>
internal sealed class LoopShard : IDisposable
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan OverrunLogInterval = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly double _stepSeconds;
    private readonly long _period;
    private readonly int _maxCatchUp;
    private readonly Lock _pendingGate = new();
    private readonly List<LoopRegistration> _pending = [];
    private readonly List<LoopRegistration> _entries = [];
    private readonly AutoResetEvent _wake = new(initialState: false);
    private readonly DurationHistogram _lateness = new();
    private readonly DurationHistogram _stepCost = new();

    private bool _gridStarted;
    private long _epoch;
    private long _nextCycle;
    private long _lastOverrunLog;
    private bool _overrunLogged;
    private long _droppedSinceLog;
    private int _plantCount;
    private long _cycles;
    private long _steps;
    private long _lateCycles;
    private long _droppedCycles;
    private long _overruns;

    public LoopShard(int index, TimeProvider time, double stepSeconds, int maxCatchUp, ILogger logger)
    {
        Index = index;
        _time = time;
        _logger = logger;
        _stepSeconds = stepSeconds;
        _period = Math.Max(1, (long)Math.Round(stepSeconds * time.TimestampFrequency));
        _maxCatchUp = maxCatchUp;
    }

    public int Index { get; }

    /// <summary>Plantas na lista, incluindo as que entram no próximo ciclo.</summary>
    public int PlantCount => Volatile.Read(ref _plantCount);

    public LoopRegistration Add(IRealTimeStepper stepper)
    {
        var registration = new LoopRegistration(stepper, Index, _time, _stepSeconds, Wake);
        lock (_pendingGate)
        {
            _pending.Add(registration);
        }

        Interlocked.Increment(ref _plantCount);
        Wake();
        return registration;
    }

    public void Wake() => _wake.Set();

    /// <summary>Uma volta da thread: espera até o próximo ciclo previsto ou executa um ciclo.</summary>
    public void RunIteration(IPreciseWaiter waiter)
    {
        ApplyChanges();
        if (_entries.Count == 0)
        {
            // Sem plantas, a grade recomeça quando a próxima chegar; o tempo parado não conta como atraso.
            _gridStarted = false;
            _wake.WaitOne(IdleWait);
            return;
        }

        long now = _time.GetTimestamp();
        if (!_gridStarted)
        {
            _epoch = now;
            _nextCycle = 0;
            _gridStarted = true;
        }

        long due = _epoch + (_nextCycle * _period);
        if (now < due)
        {
            waiter.Wait(TimeSpan.FromSeconds((double)(due - now) / _time.TimestampFrequency));
            return;
        }

        long behind = (now - due) / _period;
        if (behind > _maxCatchUp)
        {
            long dropped = behind - _maxCatchUp;
            _nextCycle += dropped;
            due = _epoch + (_nextCycle * _period);
            Interlocked.Add(ref _droppedCycles, dropped);
            Interlocked.Increment(ref _overruns);
            LogOverrun(now, dropped);
        }

        if (behind > 0)
        {
            Interlocked.Increment(ref _lateCycles);
        }

        RunCycle(due);
        _nextCycle++;
        Interlocked.Increment(ref _cycles);
    }

    public void AddHistogramsTo(DurationHistogram lateness, DurationHistogram stepCost)
    {
        _lateness.AddTo(lateness);
        _stepCost.AddTo(stepCost);
    }

    public (long Cycles, long Steps, long LateCycles, long DroppedCycles, long Overruns) GetCounters() =>
        (Interlocked.Read(ref _cycles),
         Interlocked.Read(ref _steps),
         Interlocked.Read(ref _lateCycles),
         Interlocked.Read(ref _droppedCycles),
         Interlocked.Read(ref _overruns));

    public void Dispose() => _wake.Dispose();

    private void ApplyChanges()
    {
        lock (_pendingGate)
        {
            if (_pending.Count > 0)
            {
                _entries.AddRange(_pending);
                _pending.Clear();
            }
        }

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].IsRemoved)
            {
                _entries.RemoveAt(i);
                Interlocked.Decrement(ref _plantCount);
            }
        }
    }

    private void RunCycle(long due)
    {
        foreach (LoopRegistration entry in _entries)
        {
            if (entry.IsRemoved)
            {
                continue;
            }

            long start = _time.GetTimestamp();
            _lateness.Record(ToMicroseconds(start - due));
            try
            {
                entry.Stepper.Step();
                entry.OnStepped(due);
                Interlocked.Increment(ref _steps);
            }
            catch (Exception ex)
            {
                entry.MarkRemoved();
                SessionsLog.StepperFaulted(_logger, ex, entry.Stepper.Name, Index);
                NotifyFault(entry, ex);
            }

            _stepCost.Record(ToMicroseconds(_time.GetTimestamp() - start));
        }
    }

    private void NotifyFault(LoopRegistration entry, Exception fault)
    {
        try
        {
            entry.Stepper.OnFault(fault);
        }
        catch (Exception ex)
        {
            SessionsLog.FaultHandlerFailed(_logger, ex, entry.Stepper.Name);
        }
    }

    private void LogOverrun(long now, long dropped)
    {
        _droppedSinceLog += dropped;
        if (_overrunLogged && _time.GetElapsedTime(_lastOverrunLog, now) < OverrunLogInterval)
        {
            return;
        }

        SessionsLog.CyclesDropped(_logger, Index, _droppedSinceLog, Interlocked.Read(ref _droppedCycles));
        _overrunLogged = true;
        _lastOverrunLog = now;
        _droppedSinceLog = 0;
    }

    private double ToMicroseconds(long timestampDelta) => timestampDelta * 1_000_000.0 / _time.TimestampFrequency;
}
