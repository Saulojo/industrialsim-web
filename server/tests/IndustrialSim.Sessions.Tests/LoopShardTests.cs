using IndustrialSim.Sessions.RealTime;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialSim.Sessions.Tests;

/// <summary>
/// Agendamento de uma thread do laço com relógio falso: os tempos são exatos e o resultado não depende da
/// carga da máquina.
/// </summary>
public class LoopShardTests
{
    private const double StepSeconds = 0.01;
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(StepSeconds);

    [Fact]
    public void OnSchedule_EveryPlantStepsOncePerCycle_WithoutLatenessOrDrift()
    {
        var (time, waiter, shard) = Create(maxCatchUp: 10);
        var a = new CountingStepper("a");
        var b = new CountingStepper("b");
        LoopRegistration ra = shard.Add(a);
        shard.Add(b);

        RunCycles(shard, waiter, 100);

        Assert.Equal(100, a.Steps);
        Assert.Equal(100, b.Steps);
        Assert.Equal(100, ra.StepCount);
        var counters = shard.GetCounters();
        Assert.Equal(200, counters.Steps);
        Assert.Equal(0, counters.LateCycles);
        Assert.Equal(0, counters.DroppedCycles);
        Assert.Equal(0.0, Summaries(shard).Lateness.MaxMs);

        // O 100º passo foi o do ciclo 99: o relógio está em 99 dt desde o primeiro, e a planta simulou 100 dt.
        RealTimeTracking tracking = ra.GetTracking();
        Assert.Equal(99 * StepSeconds, tracking.WallSeconds, 9);
        Assert.Equal(100 * StepSeconds, tracking.SimulatedSeconds, 9);

        // Esperando até o horário do ciclo seguinte, relógio e simulação empatam.
        time.Advance(Step);
        Assert.Equal(0.0, ra.GetTracking().Drift, 9);
    }

    [Fact]
    public void AfterA35msStall_TheLateCyclesRunBackToBack_AndNothingIsLost()
    {
        var (time, waiter, shard) = Create(maxCatchUp: 10);
        var slow = new CountingStepper { OnStep = step => { if (step == 10) { time.Advance(TimeSpan.FromMilliseconds(35)); } } };
        LoopRegistration registration = shard.Add(slow);

        RunCycles(shard, waiter, 100);

        var counters = shard.GetCounters();
        Assert.Equal(100, slow.Steps);

        // Passo 10 terminou 35 ms depois do seu horário: os ciclos 11 e 12 rodam com atraso de 1 dt ou mais e o
        // 13 com 5 ms; daí em diante, tudo no horário.
        Assert.Equal(2, counters.LateCycles);
        Assert.Equal(0, counters.DroppedCycles);
        Assert.Equal(0, counters.Overruns);
        Assert.Equal(25.0, Summaries(shard).Lateness.MaxMs, 6);

        time.Advance(Step);
        Assert.Equal(0.0, registration.GetTracking().Drift, 9);
    }

    [Fact]
    public void AfterA500msStall_OnlyMaxCatchUpCyclesAreRecovered_AndTheRestIsDropped()
    {
        var (time, waiter, shard) = Create(maxCatchUp: 10);
        var slow = new CountingStepper { OnStep = step => { if (step == 10) { time.Advance(TimeSpan.FromMilliseconds(500)); } } };
        LoopRegistration registration = shard.Add(slow);

        RunCycles(shard, waiter, 100);

        // O passo 10 terminou em 60 dt; o ciclo 11 estava 49 dt atrasado: 39 descartados, os 10 seguintes
        // recuperados (ciclos 50 a 59) e o 60 já no horário.
        var counters = shard.GetCounters();
        Assert.Equal(39, counters.DroppedCycles);
        Assert.Equal(1, counters.Overruns);
        Assert.Equal(10, counters.LateCycles);
        Assert.Equal(100, slow.Steps);

        // A planta ficou 39 passos atrás do relógio, e isso aparece na deriva.
        time.Advance(Step);
        RealTimeTracking tracking = registration.GetTracking();
        Assert.Equal(0.39, tracking.WallSeconds - tracking.SimulatedSeconds, 9);
        Assert.True(tracking.Drift > 0.0);
    }

    [Fact]
    public void APlantThatThrows_LeavesTheThread_AndTheOthersKeepStepping()
    {
        var (_, waiter, shard) = Create(maxCatchUp: 10);
        var faulty = new CountingStepper("falha") { OnStep = step => { if (step == 5) { throw new InvalidOperationException("falha de teste"); } } };
        var healthy = new CountingStepper("sã");
        LoopRegistration faultyRegistration = shard.Add(faulty);
        shard.Add(healthy);

        RunCycles(shard, waiter, 50);

        Assert.Equal(6, faulty.Steps);
        Assert.Equal(5, faultyRegistration.StepCount);
        Assert.Equal(1, faulty.Faults);
        Assert.True(faultyRegistration.IsRemoved);
        Assert.Equal(50, healthy.Steps);
        Assert.Equal(1, shard.PlantCount);
    }

    [Fact]
    public void ARemovedPlant_IsNotSteppedAgain()
    {
        var (_, waiter, shard) = Create(maxCatchUp: 10);
        var removed = new CountingStepper();
        var kept = new CountingStepper();
        LoopRegistration registration = shard.Add(removed);
        shard.Add(kept);

        RunCycles(shard, waiter, 10);
        registration.Dispose();
        RunCycles(shard, waiter, 10);

        Assert.Equal(10, removed.Steps);
        Assert.Equal(20, kept.Steps);
        Assert.Equal(1, shard.PlantCount);
    }

    [Fact]
    public void TimeWithoutPlants_DoesNotCountAsLateness()
    {
        var (time, waiter, shard) = Create(maxCatchUp: 10);
        var first = new CountingStepper();
        LoopRegistration registration = shard.Add(first);
        RunCycles(shard, waiter, 5);
        registration.Dispose();
        shard.RunIteration(waiter);

        // Um segundo sem plantas; depois chega uma nova.
        time.Advance(TimeSpan.FromSeconds(1));
        var second = new CountingStepper();
        shard.Add(second);
        RunCycles(shard, waiter, 5);

        var counters = shard.GetCounters();
        Assert.Equal(0, counters.DroppedCycles);
        Assert.Equal(0, counters.LateCycles);
        Assert.Equal(5, second.Steps);
    }

    private static (ManualTimeProvider Time, ManualWaiter Waiter, LoopShard Shard) Create(int maxCatchUp)
    {
        var time = new ManualTimeProvider();
        var waiter = new ManualWaiter(time);
        var shard = new LoopShard(0, time, StepSeconds, maxCatchUp, NullLogger.Instance);
        return (time, waiter, shard);
    }

    private static void RunCycles(LoopShard shard, IPreciseWaiter waiter, int cycles)
    {
        long target = shard.GetCounters().Cycles + cycles;
        for (int guard = 0; shard.GetCounters().Cycles < target; guard++)
        {
            Assert.True(guard < 100_000, "O laço não avançou.");
            shard.RunIteration(waiter);
        }
    }

    private static (DurationSummary Lateness, DurationSummary Cost) Summaries(LoopShard shard)
    {
        var lateness = new DurationHistogram();
        var cost = new DurationHistogram();
        shard.AddHistogramsTo(lateness, cost);
        return (lateness.Summarize(), cost.Summarize());
    }
}
