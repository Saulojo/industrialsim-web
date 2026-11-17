using System.Diagnostics;
using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Sessions.Tests;

/// <summary>O laço com o relógio e as threads de verdade. Tolerâncias largas: o CI roda em máquinas compartilhadas.</summary>
public class RealTimeLoopTests
{
    [Fact]
    public async Task RunsPlantsInRealTime_AndStopsAllThreads()
    {
        var loop = new RealTimeLoop(new RealTimeLoopOptions { ThreadCount = 2 });
        var steppers = Enumerable.Range(0, 4).Select(i => new CountingStepper($"p{i}")).ToArray();
        LoopRegistration[] registrations = [.. steppers.Select(loop.Add)];

        loop.Start();
        Assert.Equal(2, loop.AliveThreadCount);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        LoopMetrics metrics = loop.GetMetrics();
        RealTimeTracking tracking = registrations[0].GetTracking();
        var stopwatch = Stopwatch.StartNew();
        loop.Dispose();
        stopwatch.Stop();

        Assert.Equal(0, loop.AliveThreadCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Dispose levou {stopwatch.Elapsed}.");
        Assert.Equal(4, metrics.PlantCount);
        Assert.Equal(2, registrations.Select(r => r.ThreadIndex).Distinct().Count());
        Assert.All(steppers, s => Assert.InRange(s.Steps, 80, 110));
        Assert.True(Math.Abs(tracking.Drift) < 0.05, $"Deriva de {tracking.Drift:P2} em {tracking.WallSeconds:0.###} s.");
    }

    [Fact]
    public async Task ConcurrentDispose_ReturnsOnlyAfterTheThreadsStop()
    {
        // O host pode parar por dois caminhos ao mesmo tempo; nenhum dos dois pode voltar com threads vivas.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var loop = new RealTimeLoop(new RealTimeLoopOptions { ThreadCount = 2 });
            loop.Add(new CountingStepper());
            loop.Start();
            using var start = new Barrier(2);

            Task<int>[] disposers =
            [
                .. Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
                {
                    start.SignalAndWait();
                    loop.Dispose();
                    return loop.AliveThreadCount;
                })),
            ];

            int[] alive = await Task.WhenAll(disposers);
            Assert.Equal([0, 0], alive);
        }
    }

    [Fact]
    public void ThreadsAreNamedBackgroundThreads()
    {
        using var loop = new RealTimeLoop(new RealTimeLoopOptions { ThreadCount = 3 });

        Assert.Equal(["IndustrialSim.Loop.0", "IndustrialSim.Loop.1", "IndustrialSim.Loop.2"], loop.ThreadNames);
    }

    [Fact]
    public void AddAfterDispose_Throws()
    {
        var loop = new RealTimeLoop();
        loop.Start();
        loop.Dispose();

        Assert.Throws<ObjectDisposedException>(() => loop.Add(new CountingStepper()));
        Assert.Equal(0, loop.AliveThreadCount);
    }

    [Fact]
    public void PlantsGoToTheLeastLoadedThread()
    {
        using var loop = new RealTimeLoop(new RealTimeLoopOptions { ThreadCount = 3 });

        int[] threads = [.. Enumerable.Range(0, 7).Select(_ => loop.Add(new CountingStepper()).ThreadIndex)];

        Assert.Equal([0, 1, 2, 0, 1, 2, 0], threads);
    }

    [Fact]
    public void Windows_UsesTheHighResolutionTimer_WithSubMillisecondError()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Só no Windows, sistema do servidor do laboratório (D10).");

        using IPreciseWaiter waiter = PreciseWaiter.Create();
        Assert.Equal("windows-high-resolution-timer", waiter.Kind);

        // A precisão é medida na máquina de verdade (e no ensaio de carga). No CI, a VM compartilhada roda os
        // projetos de teste em paralelo e o Windows Server escalona com fatias longas: a thread acorda com até
        // ~10 ms de atraso, o que não diz nada sobre o PC do laboratório.
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true",
            "Precisão do temporizador não é medida na VM compartilhada do CI.");

        var errors = new List<double>();
        for (int i = 0; i < 50; i++)
        {
            long start = Stopwatch.GetTimestamp();
            waiter.Wait(TimeSpan.FromMilliseconds(2));
            errors.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds - 2.0);
        }

        errors.Sort();
        double median = errors[errors.Count / 2];
        Assert.True(median >= -0.1, $"Acordou cedo: mediana {median:0.###} ms.");
        Assert.True(median < 1.0, $"Erro mediano de {median:0.###} ms numa espera de 2 ms.");
    }
}
