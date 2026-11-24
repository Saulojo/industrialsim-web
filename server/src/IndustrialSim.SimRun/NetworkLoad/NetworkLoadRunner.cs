using System.Diagnostics;
using System.Globalization;
using System.Net;
using IndustrialSim.Contracts;

namespace IndustrialSim.SimRun.NetworkLoad;

/// <summary>Metas de um patamar do teste de carga (Etapa 9A; RNF04 e RNF05).</summary>
/// <param name="MaxDrift">Deriva máxima entre tempo simulado e de relógio, em qualquer planta.</param>
/// <param name="MaxP99LatenessMs">Atraso do início do passo, p99.</param>
/// <param name="MaxVmCpu">CPU média da VM inteira, como fração de todos os núcleos.</param>
/// <param name="MinMemAvailable">Menor fração de RAM disponível na VM.</param>
internal sealed record LoadGoals(double MaxDrift = 0.001, double MaxP99LatenessMs = 5.0, double MaxVmCpu = 0.8, double MinMemAvailable = 0.15);

/// <summary>Estatística simples de durações, em ms.</summary>
internal sealed record DurationsMs(long Count, double P50, double P99, double Max)
{
    public static DurationsMs From(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return new DurationsMs(0, 0, 0, 0);
        }

        double[] sorted = [.. values.Order()];
        return new DurationsMs(sorted.Length, Percentile(sorted, 0.50), Percentile(sorted, 0.99), sorted[^1]);
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
}

/// <summary>Resultado de um patamar: N plantas e N mestres por um tempo fixo.</summary>
internal sealed record PhaseReport(
    int Plants,
    int PlantsCreated,
    string? CreateError,
    double WallSeconds,
    bool ServiceRestarted,
    LoopMetricsResponse? Loop,
    double MaxDrift,
    int PlantFaults,
    long ModbusScans,
    long ModbusErrors,
    long ModbusReconnects,
    string? LastModbusError,
    DurationsMs ModbusScanMs,
    int HttpErrors,
    ResourceSummary? Resources,
    double ToolCores,
    IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}

/// <summary>
/// Teste de carga pela rede (Etapa 9A): para cada patamar, reinicia o serviço (com ssh), cria N plantas pela API,
/// autoriza o IP do mestre, roda N mestres Modbus TCP, amostra CPU e RAM da VM e, no fim, confere deriva, atraso do
/// passo, erros e recursos contra as metas. O teste crescente para no primeiro patamar que falha.
/// </summary>
internal sealed class NetworkLoadRunner(
    LoadServerClient server,
    IPAddress modbusHost,
    IPAddress masterAddress,
    RemoteHost? remote,
    TextWriter output)
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(90);

    public LoadGoals Goals { get; init; } = new();

    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(5);

    public async Task<PhaseReport> RunPhaseAsync(int plants, string scenario, TimeSpan duration, CancellationToken cancellationToken)
    {
        int httpErrors = 0;
        bool restarted = false;
        if (remote is not null)
        {
            output.WriteLine($"Reiniciando o serviço '{remote.ServiceName}' em {remote.Target} ...");
            string? restartError = await remote.RestartServiceAsync(cancellationToken).ConfigureAwait(false);
            if (restartError is not null)
            {
                return Aborted(plants, 0, restartError, httpErrors);
            }

            restarted = true;
        }

        if (!await WaitForHealthAsync(cancellationToken).ConfigureAwait(false))
        {
            return Aborted(plants, 0, "o servidor não respondeu em /api/health", httpErrors);
        }

        var created = new List<PlantSummary>(plants);
        try
        {
            for (int i = 0; i < plants; i++)
            {
                (PlantSummary? plant, string? error) = await server.CreatePlantAsync(scenario, cancellationToken).ConfigureAwait(false);
                if (plant is null)
                {
                    return Aborted(plants, created.Count, $"criação da planta {i + 1}: {error}", httpErrors);
                }

                created.Add(plant);
                string? authorizeError = await server.AuthorizeMasterAsync(plant.Id, masterAddress, cancellationToken).ConfigureAwait(false);
                if (authorizeError is not null)
                {
                    return Aborted(plants, created.Count, $"autorização do mestre {masterAddress} na planta {i + 1}: {authorizeError}", httpErrors);
                }
            }

            return await RunMastersAsync(created, duration, restarted, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (PlantSummary plant in created)
            {
                try
                {
                    await server.EndPlantAsync(plant.Id, CancellationToken.None).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // O próximo reinício do serviço limpa o que sobrar.
                }
            }
        }
    }

    private async Task<PhaseReport> RunMastersAsync(List<PlantSummary> plants, TimeSpan duration, bool restarted, CancellationToken cancellationToken)
    {
        int httpErrors = 0;
        LoadMaster[] masters = [.. plants.Select(p => new LoadMaster(new IPEndPoint(modbusHost, p.ModbusPort)))];
        var stats = new ResourceStats();
        using RemoteSampler? sampler = remote?.StartSampler(SampleInterval, stats);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task[] running = [.. masters.Select(m => m.RunAsync(stop.Token))];

        using Process tool = Process.GetCurrentProcess();
        TimeSpan toolCpuStart = tool.TotalProcessorTime;
        long start = Stopwatch.GetTimestamp();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Patamar de {plants.Count} plantas e {masters.Length} mestres por {duration.TotalMinutes:0.##} min ..."));

        TimeSpan progressEvery = duration >= TimeSpan.FromMinutes(2) ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5);
        TimeSpan nextProgress = progressEvery;
        while (true)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            if (elapsed >= duration)
            {
                break;
            }

            await Task.Delay(Min(duration - elapsed, TimeSpan.FromMilliseconds(250)), cancellationToken).ConfigureAwait(false);
            if (Stopwatch.GetElapsedTime(start) >= nextProgress)
            {
                nextProgress += progressEvery;
                LoopMetricsResponse? partial = await TryGetMetrics(cancellationToken).ConfigureAwait(false);
                ResourceSummary? resources = stats.Summarize();
                string p99 = partial is null ? "?" : partial.StepLateness.P99Ms.ToString("0.00", CultureInfo.InvariantCulture);
                string vmCpu = resources is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"  CPU da VM = {resources.VmCpuMean * 100:0.0} %");
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  t = {Stopwatch.GetElapsedTime(start).TotalSeconds,5:0} s  atraso p99 = {p99} ms  varreduras = {masters.Sum(m => m.Scans)}  erros Modbus = {masters.Sum(m => m.Errors)}{vmCpu}"));
            }
        }

        double wallSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        double toolCores = (tool.TotalProcessorTime - toolCpuStart).TotalSeconds / wallSeconds;

        // Estado das plantas e métricas antes de parar os mestres: o fim da conexão não entra na medida.
        double maxDrift = 0.0;
        int faults = 0;
        foreach (PlantSummary plant in plants)
        {
            PlantStateResponse? state = await TryGetState(plant.Id, cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                httpErrors++;
                continue;
            }

            maxDrift = Math.Max(maxDrift, Math.Abs(state.RealTime.Drift));
            if (state.FaultMessage is not null)
            {
                faults++;
            }
        }

        LoopMetricsResponse? loop = await TryGetMetrics(cancellationToken).ConfigureAwait(false);
        if (loop is null)
        {
            httpErrors++;
        }

        await stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(running).ConfigureAwait(false);

        ResourceSummary? summary = stats.Summarize();
        var report = new PhaseReport(
            plants.Count,
            plants.Count,
            null,
            wallSeconds,
            restarted,
            loop,
            maxDrift,
            faults,
            masters.Sum(m => m.Scans),
            masters.Sum(m => m.Errors),
            masters.Sum(m => m.Reconnects),
            masters.Select(m => m.LastError).LastOrDefault(e => e is not null),
            DurationsMs.From([.. masters.SelectMany(m => m.ScanDurations())]),
            httpErrors,
            summary,
            toolCores,
            []);
        return report with { Failures = Evaluate(report, Goals, expectResources: remote is not null) };
    }

    /// <summary>Metas que o patamar não cumpriu (vazio se cumpriu todas).</summary>
    internal static IReadOnlyList<string> Evaluate(PhaseReport report, LoadGoals goals, bool expectResources)
    {
        var failures = new List<string>();
        if (report.CreateError is not null)
        {
            failures.Add($"patamar interrompido: {report.CreateError}");
            return failures;
        }

        if (report.MaxDrift >= goals.MaxDrift)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"deriva {report.MaxDrift * 100:0.0000} % (meta < {goals.MaxDrift * 100:0.0} %)"));
        }

        if (report.Loop is null)
        {
            failures.Add("métricas do laço indisponíveis");
        }
        else if (report.Loop.StepLateness.P99Ms >= goals.MaxP99LatenessMs)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"atraso do passo p99 {report.Loop.StepLateness.P99Ms:0.00} ms (meta < {goals.MaxP99LatenessMs:0} ms)"));
        }

        long errors = report.ModbusErrors + report.HttpErrors + report.PlantFaults;
        if (errors > 0)
        {
            failures.Add($"erros: {report.ModbusErrors} Modbus, {report.HttpErrors} HTTP, {report.PlantFaults} plantas em falha");
        }

        if (report.ModbusScans == 0)
        {
            failures.Add("nenhuma varredura Modbus completa");
        }

        if (report.Resources is { } resources)
        {
            if (resources.VmCpuMean >= goals.MaxVmCpu)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"CPU da VM {resources.VmCpuMean * 100:0.0} % (meta < {goals.MaxVmCpu * 100:0} %)"));
            }

            if (resources.MemAvailableMinFraction < goals.MinMemAvailable)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"RAM disponível {resources.MemAvailableMinFraction * 100:0.0} % (meta ≥ {goals.MinMemAvailable * 100:0} %)"));
            }
        }
        else if (expectResources)
        {
            failures.Add("sem amostras de CPU e RAM da VM (ssh)");
        }

        return failures;
    }

    private PhaseReport Aborted(int plants, int created, string error, int httpErrors)
    {
        var report = new PhaseReport(plants, created, error, 0, false, null, 0, 0, 0, 0, 0, null, DurationsMs.From([]), httpErrors, null, 0, []);
        return report with { Failures = Evaluate(report, Goals, expectResources: false) };
    }

    private async Task<bool> WaitForHealthAsync(CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < HealthTimeout)
        {
            if (await server.TryGetHealthAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<LoopMetricsResponse?> TryGetMetrics(CancellationToken cancellationToken)
    {
        try
        {
            return await server.GetMetricsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<PlantStateResponse?> TryGetState(Guid plant, CancellationToken cancellationToken)
    {
        try
        {
            return await server.GetStateAsync(plant, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
