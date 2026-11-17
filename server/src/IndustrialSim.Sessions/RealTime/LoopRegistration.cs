namespace IndustrialSim.Sessions.RealTime;

/// <summary>
/// Uma planta na lista de uma thread do laço. <see cref="Dispose"/> a retira: a thread para de chamá-la no
/// ciclo seguinte. Para garantir que nenhum passo começa depois de retirar, o próprio item deve recusar passos
/// (como faz <see cref="IndustrialSim.Sessions.PlantInstance"/>).
/// </summary>
public sealed class LoopRegistration : IDisposable
{
    private readonly TimeProvider _time;
    private readonly double _stepSeconds;
    private readonly Action _wake;
    private long _steps;
    private long _startTimestamp;
    private int _started;
    private int _removed;

    internal LoopRegistration(IRealTimeStepper stepper, int threadIndex, TimeProvider time, double stepSeconds, Action wake)
    {
        Stepper = stepper;
        ThreadIndex = threadIndex;
        _time = time;
        _stepSeconds = stepSeconds;
        _wake = wake;
    }

    public IRealTimeStepper Stepper { get; }

    /// <summary>Thread do laço que avança esta planta.</summary>
    public int ThreadIndex { get; }

    public bool IsRemoved => Volatile.Read(ref _removed) != 0;

    /// <summary>Passos executados pelo laço.</summary>
    public long StepCount => Interlocked.Read(ref _steps);

    /// <summary>Quanto a planta acompanhou o relógio desde o horário previsto do primeiro passo.</summary>
    public RealTimeTracking GetTracking()
    {
        if (Volatile.Read(ref _started) == 0)
        {
            return new RealTimeTracking(0, 0.0, 0.0, 0.0);
        }

        long start = Interlocked.Read(ref _startTimestamp);
        long steps = StepCount;
        double wall = (double)(_time.GetTimestamp() - start) / _time.TimestampFrequency;
        double simulated = steps * _stepSeconds;
        double drift = wall > 0.0 ? (wall - simulated) / wall : 0.0;
        return new RealTimeTracking(steps, simulated, wall, drift);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _removed, 1) == 0)
        {
            _wake();
        }
    }

    internal void MarkRemoved() => Volatile.Write(ref _removed, 1);

    /// <summary>Chamado pela thread do laço depois de cada passo, com o horário previsto do ciclo.</summary>
    internal void OnStepped(long scheduledTimestamp)
    {
        if (Volatile.Read(ref _started) == 0)
        {
            Interlocked.Exchange(ref _startTimestamp, scheduledTimestamp);
            Volatile.Write(ref _started, 1);
        }

        Interlocked.Increment(ref _steps);
    }
}
