using System.Threading.Channels;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.Sessions;

/// <summary>Um alarme ativo.</summary>
/// <param name="Signal">Saída externa do cenário ou <see cref="PlantAlarms.WatchdogSignal"/>.</param>
/// <param name="SinceSeconds">Tempo simulado em que ficou ativo (desde o último reinício).</param>
/// <param name="SinceUtc">Horário em que ficou ativo.</param>
public sealed record ActiveAlarmInfo(string Signal, double SinceSeconds, DateTimeOffset SinceUtc);

/// <summary>Alarmes ativos; <paramref name="Version"/> muda a cada entrada ou saída de alarme.</summary>
public sealed record AlarmSet(long Version, IReadOnlyList<ActiveAlarmInfo> Active)
{
    public static readonly AlarmSet Empty = new(0, []);
}

/// <summary>
/// Estado de uma planta num passo, para quem acompanha a planta ao vivo (o WebSocket da IHM): entradas e saídas
/// externas em SI (D06), na ordem do cenário, e os alarmes ativos.
/// </summary>
/// <param name="StepCount">Passos desde o último reinício.</param>
/// <param name="SimulatedSeconds">Tempo simulado desde o último reinício.</param>
/// <param name="Run">Número do reinício (0 até o primeiro).</param>
/// <param name="Status">Situação da planta.</param>
/// <param name="WatchdogTripped">Se o watchdog está disparado.</param>
/// <param name="Inputs">Entradas externas.</param>
/// <param name="Outputs">Saídas externas.</param>
/// <param name="Alarms">Alarmes ativos.</param>
public sealed record PlantSnapshot(
    long StepCount,
    double SimulatedSeconds,
    int Run,
    InstanceStatus Status,
    bool WatchdogTripped,
    double[] Inputs,
    double[] Outputs,
    AlarmSet Alarms);

/// <summary>
/// Assinatura do estado de uma planta: recebe um <see cref="PlantSnapshot"/> a cada período de publicação. A
/// fila guarda só o mais recente: um leitor lento pula estados, mas nunca atrasa o laço. A fila é completada
/// quando a planta é encerrada. Descartar a assinatura a retira da planta.
/// </summary>
public sealed class PlantSubscription : IDisposable
{
    private readonly Channel<PlantSnapshot> _channel = Channel.CreateBounded<PlantSnapshot>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });

    private readonly Action<PlantSubscription> _remove;
    private int _disposed;

    internal PlantSubscription(Action<PlantSubscription> remove) => _remove = remove;

    public ChannelReader<PlantSnapshot> Reader => _channel.Reader;

    internal void Publish(PlantSnapshot snapshot) => _channel.Writer.TryWrite(snapshot);

    internal void Complete() => _channel.Writer.TryComplete();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _remove(this);
            Complete();
        }
    }
}

/// <summary>
/// Avalia os alarmes da seção <c>hmi</c> do cenário (D39) e o watchdog a cada passo. Usado sob a trava da planta.
/// </summary>
internal sealed class PlantAlarms
{
    /// <summary>Sinal do alarme de watchdog disparado.</summary>
    public const string WatchdogSignal = "watchdog";

    private readonly string[] _signals;
    private readonly int[] _outputIndexes;
    private readonly ActiveAlarmInfo?[] _active;
    private long _version;

    public PlantAlarms(ScenarioPlant plant)
    {
        IReadOnlyList<AlarmDefinition> alarms = plant.Definition.Hmi?.Alarms ?? [];
        _signals = [.. alarms.Select(a => a.Signal), WatchdogSignal];
        _outputIndexes = [.. alarms.Select(a => plant.OutputIndex(a.Signal)), -1];
        _active = new ActiveAlarmInfo?[_signals.Length];
        Current = AlarmSet.Empty;
    }

    /// <summary>Sinais que podem virar alarme, na ordem do cenário, com o watchdog por último.</summary>
    public IReadOnlyList<string> Signals => _signals;

    public AlarmSet Current { get; private set; }

    /// <summary>Confere os alarmes depois de um passo; refaz <see cref="Current"/> só se algum mudou.</summary>
    public void Evaluate(ScenarioPlant plant, bool watchdogTripped, TimeProvider time)
    {
        bool changed = false;
        for (int i = 0; i < _signals.Length; i++)
        {
            int index = _outputIndexes[i];
            bool active = index < 0 ? watchdogTripped : plant.GetOutput(index) >= 0.5;
            if (active == _active[i] is not null)
            {
                continue;
            }

            _active[i] = active ? new ActiveAlarmInfo(_signals[i], plant.Clock.TimeSeconds, time.GetUtcNow()) : null;
            changed = true;
        }

        if (changed)
        {
            Current = new AlarmSet(++_version, [.. _active.OfType<ActiveAlarmInfo>()]);
        }
    }

    /// <summary>Limpa os alarmes (reinício): os que continuarem ativos voltam no passo seguinte.</summary>
    public void Clear()
    {
        if (Current.Active.Count == 0)
        {
            return;
        }

        Array.Clear(_active);
        Current = new AlarmSet(++_version, []);
    }
}
