using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialSim.Sessions.RealTime;

/// <summary>
/// Laço de tempo real do servidor (RF02, RNF05): threads dedicadas que avançam as plantas em execução com passo
/// fixo, medido no relógio monotônico (<see cref="TimeProvider.GetTimestamp"/>, que no sistema é o
/// <c>Stopwatch</c>). Cada planta fica numa thread só, escolhida entre as menos ocupadas quando ela entra.
/// </summary>
/// <remarks>
/// As threads são de fundo e com prioridade acima do normal. <see cref="Dispose"/> pede que parem e espera cada
/// uma terminar (no máximo <see cref="RealTimeLoopOptions.ShutdownTimeout"/>), para o processo encerrar sem
/// threads penduradas.
/// </remarks>
public sealed class RealTimeLoop : IDisposable
{
    private const int Created = 0;
    private const int Running = 1;
    private const int Disposed = 2;

    private readonly LoopShard[] _shards;
    private readonly Thread[] _threads;
    private readonly Func<IPreciseWaiter> _waiterFactory;
    private readonly ILogger _logger;
    private readonly Lock _addGate = new();

    // Sinalizado quando o primeiro Dispose termina; não é descartado, para quem espera nele.
    private readonly ManualResetEventSlim _stopped = new(initialState: false);
    private volatile bool _stopping;
    private int _state;
    private string _waiterKind = "not-started";

    /// <param name="options">Configuração; por padrão, passo de 10 ms e duas threads.</param>
    /// <param name="logger">Logger; por padrão, nenhum.</param>
    /// <param name="time">Relógio monotônico; os testes passam um relógio falso.</param>
    /// <param name="waiterFactory">Cria a espera de cada thread; por padrão, <see cref="PreciseWaiter.Create"/>.</param>
    public RealTimeLoop(
        RealTimeLoopOptions? options = null,
        ILogger<RealTimeLoop>? logger = null,
        TimeProvider? time = null,
        Func<IPreciseWaiter>? waiterFactory = null)
    {
        Options = options ?? new RealTimeLoopOptions();
        Options.Validate();
        _logger = logger ?? NullLogger<RealTimeLoop>.Instance;
        _waiterFactory = waiterFactory ?? PreciseWaiter.Create;
        TimeProvider clock = time ?? TimeProvider.System;

        _shards = new LoopShard[Options.ThreadCount];
        _threads = new Thread[Options.ThreadCount];
        for (int i = 0; i < _shards.Length; i++)
        {
            _shards[i] = new LoopShard(i, clock, Options.StepSeconds, Options.MaxCatchUpSteps, _logger);
        }
    }

    public RealTimeLoopOptions Options { get; }

    public bool IsRunning => Volatile.Read(ref _state) == Running;

    /// <summary>Nomes das threads, para identificá-las num depurador ou num dump.</summary>
    public IReadOnlyList<string> ThreadNames => [.. _shards.Select(s => ThreadName(s.Index))];

    /// <summary>Threads do laço que ainda estão vivas (zero depois de <see cref="Dispose"/>).</summary>
    public int AliveThreadCount => _threads.Count(t => t is { IsAlive: true });

    /// <summary>Cria e inicia as threads. Plantas podem ser adicionadas antes ou depois.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, Running, Created) != Created)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _state) == Disposed, this);
            return;
        }

        using (IPreciseWaiter probe = _waiterFactory())
        {
            _waiterKind = probe.Kind;
        }

        for (int i = 0; i < _shards.Length; i++)
        {
            LoopShard shard = _shards[i];
            _threads[i] = new Thread(() => Run(shard))
            {
                IsBackground = true,
                Name = ThreadName(i),
                Priority = ThreadPriority.AboveNormal,
            };
            _threads[i].Start();
        }

        SessionsLog.LoopStarted(_logger, _shards.Length, Options.StepSeconds * 1000.0, Options.MaxCatchUpSteps, _waiterKind);
    }

    /// <summary>Põe uma planta na thread com menos plantas; ela dá o primeiro passo no próximo ciclo.</summary>
    public LoopRegistration Add(IRealTimeStepper stepper)
    {
        ArgumentNullException.ThrowIfNull(stepper);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _state) == Disposed, this);

        lock (_addGate)
        {
            LoopShard target = _shards.MinBy(s => s.PlantCount)!;
            return target.Add(stepper);
        }
    }

    public LoopMetrics GetMetrics()
    {
        var lateness = new DurationHistogram();
        var stepCost = new DurationHistogram();
        long cycles = 0;
        long steps = 0;
        long late = 0;
        long dropped = 0;
        long overruns = 0;
        int plants = 0;
        foreach (LoopShard shard in _shards)
        {
            var counters = shard.GetCounters();
            cycles += counters.Cycles;
            steps += counters.Steps;
            late += counters.LateCycles;
            dropped += counters.DroppedCycles;
            overruns += counters.Overruns;
            plants += shard.PlantCount;
            shard.AddHistogramsTo(lateness, stepCost);
        }

        return new LoopMetrics(
            _shards.Length,
            plants,
            _waiterKind,
            cycles,
            steps,
            late,
            dropped,
            overruns,
            lateness.Summarize(),
            stepCost.Summarize());
    }

    /// <summary>
    /// Para as threads e espera cada uma terminar. Uma chamada simultânea (o host pode parar por dois caminhos ao
    /// mesmo tempo) espera a primeira terminar, para que ninguém volte com threads ainda vivas.
    /// </summary>
    public void Dispose()
    {
        int previous = Interlocked.Exchange(ref _state, Disposed);
        if (previous == Disposed)
        {
            _stopped.Wait(Options.ShutdownTimeout * (_threads.Length + 1));
            return;
        }

        _stopping = true;
        foreach (LoopShard shard in _shards)
        {
            shard.Wake();
        }

        foreach (Thread? thread in _threads)
        {
            if (thread is not null && !thread.Join(Options.ShutdownTimeout))
            {
                SessionsLog.ThreadDidNotStop(_logger, thread.Name ?? string.Empty, Options.ShutdownTimeout.TotalSeconds);
            }
        }

        int alive = AliveThreadCount;
        if (alive == 0)
        {
            foreach (LoopShard shard in _shards)
            {
                shard.Dispose();
            }
        }

        if (previous == Running)
        {
            SessionsLog.LoopStopped(_logger, alive);
        }

        _stopped.Set();
    }

    private static string ThreadName(int index) => $"IndustrialSim.Loop.{index}";

    private void Run(LoopShard shard)
    {
        using IPreciseWaiter waiter = _waiterFactory();
        while (!_stopping)
        {
            try
            {
                shard.RunIteration(waiter);
            }
            catch (Exception ex)
            {
                // Erro do próprio laço (não de uma planta): registra e segue, sem girar em falso.
                SessionsLog.LoopIterationFailed(_logger, ex, shard.Index);
                Thread.Sleep(10);
            }
        }
    }
}
