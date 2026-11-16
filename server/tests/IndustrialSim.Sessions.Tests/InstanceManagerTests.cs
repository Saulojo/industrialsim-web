using System.Runtime.CompilerServices;
using IndustrialSim.IO;
using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Sessions.Tests;

public class InstanceManagerTests
{
    private static readonly ScenarioCatalog Catalog = ProductScenarios.Catalog();

    [Fact]
    public void CreateListGetAndEnd()
    {
        using var loop = new RealTimeLoop();
        using var manager = new InstanceManager(Catalog, loop);

        PlantInstance a = Created(manager.Create(new CreatePlantRequest("caldeira")));
        PlantInstance b = Created(manager.Create(new CreatePlantRequest("caldeira", NoiseSeed: 7, OwnerId: "usuario-1")));

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(101UL, a.NoiseSeed);
        Assert.Equal(7UL, b.NoiseSeed);
        Assert.Null(a.OwnerId);
        Assert.Equal("usuario-1", b.OwnerId);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), manager.List().Select(i => i.Id).Order());
        Assert.True(manager.TryGet(a.Id, out PlantInstance? found));
        Assert.Same(a, found);

        Assert.True(manager.End(a.Id));

        Assert.Equal(InstanceStatus.Ended, a.Status);
        Assert.Equal(EndReason.Requested, a.EndReason);
        Assert.False(manager.TryGet(a.Id, out _));
        Assert.False(manager.End(a.Id));
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void UnknownScenario_IsRefused()
    {
        using var loop = new RealTimeLoop();
        using var manager = new InstanceManager(Catalog, loop);

        CreatePlantResult result = manager.Create(new CreatePlantRequest("nao-existe"));

        Assert.Equal(CreatePlantStatus.ScenarioNotFound, result.Status);
        Assert.Null(result.Instance);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void TheInstanceLimit_IsEnforced_AndFreedByEnding()
    {
        using var loop = new RealTimeLoop();
        using var manager = new InstanceManager(Catalog, loop, new InstanceManagerOptions { MaxInstances = 3 });

        PlantInstance first = Created(manager.Create(new CreatePlantRequest("caldeira")));
        Created(manager.Create(new CreatePlantRequest("caldeira")));
        Created(manager.Create(new CreatePlantRequest("caldeira")));

        Assert.Equal(CreatePlantStatus.LimitReached, manager.Create(new CreatePlantRequest("caldeira")).Status);

        manager.End(first.Id);
        Assert.Equal(CreatePlantStatus.Created, manager.Create(new CreatePlantRequest("caldeira")).Status);
    }

    [Fact]
    public async Task PlantsRunIsolated_EachWithItsOwnIoImage()
    {
        using var loop = new RealTimeLoop();
        using var manager = new InstanceManager(Catalog, loop);
        PlantInstance a = Created(manager.Create(new CreatePlantRequest("caldeira")));
        PlantInstance b = Created(manager.Create(new CreatePlantRequest("caldeira")));
        loop.Start();

        // Só a planta A recebe "fechar FV-101" (0 contagens = 4 mA = 0 %).
        Assert.True(a.Io.Image.TryWriteHoldingRegister(0, AnalogCodec.ToRegister(0)));
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        PlantState stateA = a.GetState();
        PlantState stateB = b.GetState();
        Assert.True(stateA.StepCount > 10, $"A deu {stateA.StepCount} passos.");
        Assert.True(stateB.StepCount > 10, $"B deu {stateB.StepCount} passos.");
        Assert.Equal(0.0, Input(stateA, "FV-101_command"));
        Assert.Equal(0.8228, Input(stateB, "FV-101_command"), 3);
    }

    [Fact]
    public void IdlePlants_ExpireAfterTheConfiguredTime_UnlessAClientIsAttached()
    {
        var time = new ManualTimeProvider();
        using var loop = new RealTimeLoop();
        using var manager = new InstanceManager(Catalog, loop, new InstanceManagerOptions { IdleTimeout = TimeSpan.FromMinutes(10) }, time);
        PlantInstance alone = Created(manager.Create(new CreatePlantRequest("caldeira")));
        PlantInstance watched = Created(manager.Create(new CreatePlantRequest("caldeira")));
        PlantInstance touched = Created(manager.Create(new CreatePlantRequest("caldeira")));
        IDisposable client = watched.AttachClient();

        time.Advance(TimeSpan.FromMinutes(9));
        touched.Touch();
        Assert.Equal(0, manager.ExpireIdle());

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, manager.ExpireIdle());
        Assert.Equal(InstanceStatus.Ended, alone.Status);
        Assert.Equal(EndReason.Expired, alone.EndReason);

        // O cliente sai: a contagem recomeça a partir da saída.
        client.Dispose();
        client.Dispose();
        Assert.Equal(0, watched.ClientCount);
        time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        Assert.Equal(1, manager.ExpireIdle());
        Assert.Equal(InstanceStatus.Ended, touched.Status);
        Assert.Equal(InstanceStatus.Running, watched.Status);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, manager.ExpireIdle());
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task WithAShortTimeout_AnIdlePlantExpiresOnItsOwn_AndItsMemoryIsReleased()
    {
        using var loop = new RealTimeLoop();
        using var manager = new InstanceManager(
            Catalog,
            loop,
            new InstanceManagerOptions { IdleTimeout = TimeSpan.FromMilliseconds(200), ExpiryCheckInterval = TimeSpan.FromMilliseconds(50) });
        loop.Start();
        manager.StartExpiryTimer();

        WeakReference plant = CreateAndForget(manager);
        Assert.Equal(1, manager.Count);

        await WaitUntil(() => manager.Count == 0, "a planta não expirou");
        await WaitUntil(() => loop.GetMetrics().PlantCount == 0, "a planta não saiu do laço");
        await WaitUntil(
            () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                return !plant.IsAlive;
            },
            "a planta continua na memória");
    }

    [Fact]
    public void EndingStopsTheSteps()
    {
        var time = new ManualTimeProvider();
        var waiter = new ManualWaiter(time);
        using var loop = new RealTimeLoop(time: time, waiterFactory: () => waiter);
        using var manager = new InstanceManager(Catalog, loop, time: time);
        PlantInstance instance = Created(manager.Create(new CreatePlantRequest("caldeira")));

        // Sem iniciar o laço: um passo pela interface, depois o encerramento.
        ((IRealTimeStepper)instance).Step();
        Assert.Equal(1, instance.GetState().StepCount);
        manager.End(instance.Id);
        ((IRealTimeStepper)instance).Step();

        Assert.Equal(1, instance.GetState().StepCount);
        Assert.Equal(InstanceStatus.Ended, instance.GetState().Status);
    }

    [Fact]
    public void AFaultInTheStep_MarksThePlantFaulted()
    {
        using var loop = new RealTimeLoop();
        using var manager = new InstanceManager(Catalog, loop);
        PlantInstance instance = Created(manager.Create(new CreatePlantRequest("caldeira")));

        ((IRealTimeStepper)instance).OnFault(new InvalidOperationException("teste"));

        Assert.Equal(InstanceStatus.Faulted, instance.Status);
        Assert.Equal("teste", instance.FaultMessage);
        Assert.True(manager.TryGet(instance.Id, out _));
    }

    [Fact]
    public void Participants_AreKeptForLaterStages()
    {
        using var loop = new RealTimeLoop();
        using var manager = new InstanceManager(Catalog, loop);
        PlantInstance instance = Created(manager.Create(new CreatePlantRequest("caldeira", OwnerId: "dono")));

        Assert.True(instance.AddParticipant("b"));
        Assert.True(instance.AddParticipant("a"));
        Assert.False(instance.AddParticipant("a"));
        Assert.True(instance.RemoveParticipant("b"));

        Assert.Equal(["a"], instance.Participants);
    }

    [Fact]
    public async Task ShuttingDownWithActivePlants_EndsThemAndLeavesNoLoopThreads()
    {
        var loop = new RealTimeLoop(new RealTimeLoopOptions { ThreadCount = 2 });
        var manager = new InstanceManager(Catalog, loop);
        loop.Start();
        PlantInstance[] plants = [.. Enumerable.Range(0, 5).Select(_ => Created(manager.Create(new CreatePlantRequest("caldeira"))))];
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        manager.Dispose();
        loop.Dispose();

        Assert.All(plants, p => Assert.Equal(EndReason.Shutdown, p.EndReason));
        Assert.Equal(0, loop.AliveThreadCount);
        Assert.Throws<ObjectDisposedException>(() => manager.Create(new CreatePlantRequest("caldeira")));
    }

    private static PlantInstance Created(CreatePlantResult result)
    {
        Assert.Equal(CreatePlantStatus.Created, result.Status);
        return Assert.IsType<PlantInstance>(result.Instance);
    }

    private static double Input(PlantState state, string name) => state.Inputs.Single(i => i.Name == name).Value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndForget(InstanceManager manager) =>
        new(Created(manager.Create(new CreatePlantRequest("caldeira"))).Io);

    private static async Task WaitUntil(Func<bool> condition, string failure)
    {
        for (int i = 0; i < 100; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail(failure);
    }
}
