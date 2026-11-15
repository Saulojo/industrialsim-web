namespace IndustrialSim.IO.Tests;

/// <summary>Etapa 4: imagem de I/O dimensionada pelo mapa, thread-safe e com cópia instantânea.</summary>
public class IoImageTests(ITestOutputHelper output)
{
    /// <summary>
    /// Critério 8: a imagem da caldeira ocupa menos de 4 KB (o protótipo reservava cerca de 400 KB).
    /// Mede de duas formas: a soma dos bytes de dados e a memória que o .NET de fato aloca para o objeto
    /// (com os cabeçalhos dos vetores).
    /// </summary>
    [Fact]
    public void CaldeiraImage_TakesLessThan4Kb()
    {
        IoMap map = Caldeira.Create().Map;

        // Aquecimento: a primeira construção pode incluir alocações do próprio runtime.
        _ = new IoImage(map.CoilCount, map.DiscreteInputCount, map.HoldingRegisterCount, map.InputRegisterCount);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var image = new IoImage(map.CoilCount, map.DiscreteInputCount, map.HoldingRegisterCount, map.InputRegisterCount);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"áreas: {image.CoilCount} coils, {image.DiscreteInputCount} entradas discretas, " +
            $"{image.HoldingRegisterCount} holding registers, {image.InputRegisterCount} input registers");
        output.WriteLine($"bytes de dados: {image.SizeBytes}");
        output.WriteLine($"bytes alocados pelo .NET (objeto, trava e vetores): {allocated}");

        Assert.True(image.SizeBytes < 4096, $"dados: {image.SizeBytes} bytes");
        Assert.True(allocated < 4096, $"alocados: {allocated} bytes");
        GC.KeepAlive(image);
    }

    [Fact]
    public void AccessOutsideTheMap_FailsWithoutChangingAnything()
    {
        var image = new IoImage(coilCount: 1, discreteInputCount: 6, holdingRegisterCount: 11, inputRegisterCount: 110);
        Span<ushort> two = stackalloc ushort[2];

        Assert.False(image.TryWriteHoldingRegister(11, 1));
        Assert.False(image.TryWriteHoldingRegisters(10, [1, 2]));
        Assert.False(image.TryReadInputRegisters(109, two));
        Assert.False(image.TryReadHoldingRegisters(-1, two));
        Assert.False(image.TryWriteCoil(1, true));

        Assert.True(image.TryWriteHoldingRegisters(9, [7, 8]));
        Assert.True(image.TryReadHoldingRegisters(9, two));
        Assert.Equal([(ushort)7, (ushort)8], two.ToArray());
        Assert.True(image.TryReadInputRegisters(108, two));
    }

    /// <summary>
    /// Thread-safe: quatro threads escrevem pares de registradores (sempre dois valores iguais, numa operação só)
    /// enquanto outra tira snapshots. Nenhum snapshot pode ver um par pela metade.
    /// </summary>
    [Fact]
    public async Task ConcurrentWrites_NeverAppearHalfDoneInASnapshot()
    {
        var image = new IoImage(coilCount: 2, discreteInputCount: 0, holdingRegisterCount: 8, inputRegisterCount: 0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        long torn = 0;
        long snapshots = 0;

        Task[] writers =
        [
            .. Enumerable.Range(0, 4).Select(w => Task.Run(() =>
            {
                ushort value = (ushort)(w * 1000);
                while (!stop.IsCancellationRequested)
                {
                    value++;
                    image.TryWriteHoldingRegisters(2 * w, [value, value]);
                    image.TryWriteCoils(0, [(value & 1) == 0, (value & 1) == 0]);
                }
            }, stop.Token)),
        ];

        Task reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                IoSnapshot snapshot = image.Snapshot();
                snapshots++;
                for (int pair = 0; pair < 4; pair++)
                {
                    if (snapshot.HoldingRegisters[2 * pair] != snapshot.HoldingRegisters[(2 * pair) + 1])
                    {
                        torn++;
                    }
                }

                if (snapshot.Coils[0] != snapshot.Coils[1])
                {
                    torn++;
                }
            }
        }, stop.Token);

        await Task.Delay(500, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await Task.WhenAll([.. writers, reader]);

        output.WriteLine($"{snapshots} snapshots, {torn} com escrita pela metade");
        Assert.True(snapshots > 100, $"snapshots: {snapshots}");
        Assert.Equal(0, torn);
    }

    /// <summary>
    /// Com a planta avançando numa thread e um "mestre" escrevendo em outra, todo snapshot é de um único passo:
    /// o nível em contagens (registrador 0) e o nível do bloco de diagnóstico (float32 em 100–101) vêm da mesma
    /// medição, e o número do passo nunca volta.
    /// </summary>
    [Fact]
    public async Task SnapshotsWhileThePlantRuns_AreAlwaysFromASingleStep()
    {
        PlantIo io = Caldeira.Create();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        long incoherent = 0;
        long snapshots = 0;

        Task plant = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                io.Step();
            }
        }, stop.Token);

        Task master = Task.Run(() =>
        {
            ushort heartbeat = 0;
            while (!stop.IsCancellationRequested)
            {
                heartbeat++;
                io.Image.TryWriteHoldingRegister(Caldeira.Heartbeat, heartbeat);
                io.Image.TryWriteHoldingRegister(Caldeira.WaterValveCommand, (ushort)(heartbeat % 27648));
            }
        }, stop.Token);

        Task reader = Task.Run(() =>
        {
            long lastStep = -1;
            while (!stop.IsCancellationRequested)
            {
                IoSnapshot snapshot = io.Image.Snapshot();
                snapshots++;

                double levelM = snapshot.Float(Caldeira.Diagnostics);
                short fromDiagnostics = AnalogCodec.MilliampsToCounts(
                    CurrentLoop.Saturate(CurrentLoop.ToMilliamps(levelM, 0.0, 1.2)),
                    AnalogFormat.Counts27648);
                if (Math.Abs(fromDiagnostics - snapshot.Counts(Caldeira.Level)) > 1 || snapshot.StepCount < lastStep)
                {
                    incoherent++;
                }

                lastStep = snapshot.StepCount;
            }
        }, stop.Token);

        await Task.Delay(500, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await Task.WhenAll(plant, master, reader);

        output.WriteLine($"{io.Plant.Clock.StepCount} passos, {snapshots} snapshots, {incoherent} incoerentes");
        Assert.True(snapshots > 100, $"snapshots: {snapshots}");
        Assert.Equal(0, incoherent);
    }
}
