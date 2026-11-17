using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;
using IndustrialSim.Sessions;
using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.SimRun;

/// <summary>
/// <c>simrun carga --cenario scenario.json --plantas 30 --minutos 10</c>: ensaio de carga do laço de tempo real
/// (Etapa 5, RNF04 e RNF05). Roda N plantas do cenário em tempo real, com um "mestre" que lê as medições e
/// escreve comandos a cada 100 ms, e mede deriva, atraso do passo e CPU do processo.
/// </summary>
/// <remarks>
/// O mestre é um controle proporcional simples sobre os dois primeiros input registers (nível e pressão, na
/// caldeira) e os dois primeiros holding registers, mais o heartbeat: serve só para a planta receber comandos
/// que mudam, como receberia de um CLP. Ele roda fora da planta, como qualquer mestre externo.
/// </remarks>
internal static class LoadCommand
{
    public const string Name = "carga";

    public const string Usage =
        "  simrun carga --cenario <scenario.json> [--plantas 30] [--minutos 10] [--threads 2] [--relatorio <relatorio.json>]";

    // Metas da Etapa 5.
    public const double MaxDrift = 0.001;
    public const double MaxP99LatenessMs = 5.0;
    public const double MaxCpuOfOneCore = 0.5;

    private static readonly TimeSpan MasterPeriod = TimeSpan.FromMilliseconds(100);
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryParse(args, out Settings? settings, out string? error))
        {
            stderr.WriteLine(error ?? "Uso:");
            stderr.WriteLine(Usage);
            return SimRunApp.ExitUsage;
        }

        ScenarioDefinition definition;
        try
        {
            definition = ScenarioLoader.Load(File.ReadAllText(settings.ScenarioPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ScenarioValidationException)
        {
            stderr.WriteLine($"Cenário inválido ou ilegível ({settings.ScenarioPath}): {ex.Message}");
            return SimRunApp.ExitFailed;
        }

        if (definition.Io is null)
        {
            stderr.WriteLine($"O cenário '{definition.Id}' não tem a seção 'io'.");
            return SimRunApp.ExitFailed;
        }

        LoadReport report = RunLoad(definition, settings, stdout);
        Print(report, stdout);
        if (settings.ReportPath is not null)
        {
            File.WriteAllText(settings.ReportPath, JsonSerializer.Serialize(report, ReportJson), new UTF8Encoding(false));
            stdout.WriteLine($"Relatório gravado em {settings.ReportPath}.");
        }

        return report.AllMet ? SimRunApp.ExitOk : SimRunApp.ExitFailed;
    }

    private static LoadReport RunLoad(ScenarioDefinition definition, Settings settings, TextWriter stdout)
    {
        var catalog = new ScenarioCatalog([new ScenarioEntry(definition, settings.ScenarioPath)]);
        var loopOptions = new RealTimeLoopOptions { StepSeconds = definition.StepSeconds, ThreadCount = settings.Threads };
        using var loop = new RealTimeLoop(loopOptions);
        using var manager = new InstanceManager(catalog, loop, new InstanceManagerOptions { MaxInstances = settings.Plants });

        PlantInstance[] plants = new PlantInstance[settings.Plants];
        for (int i = 0; i < plants.Length; i++)
        {
            // Sementes diferentes: cada planta tem o seu ruído.
            CreatePlantResult created = manager.Create(new CreatePlantRequest(definition.Id, NoiseSeed: definition.NoiseSeed + (ulong)i));
            plants[i] = created.Instance ?? throw new InvalidOperationException($"Planta {i} não criada: {created.Status}.");
        }

        var master = new Master(plants);
        using Process process = Process.GetCurrentProcess();
        TimeSpan cpuStart = process.TotalProcessorTime;
        long wallStart = Stopwatch.GetTimestamp();
        loop.Start();

        TimeSpan duration = TimeSpan.FromMinutes(settings.Minutes);
        TimeSpan progressEvery = duration >= TimeSpan.FromMinutes(2) ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5);
        TimeSpan nextProgress = progressEvery;
        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Rodando {plants.Length} plantas de '{definition.Id}' por {duration.TotalMinutes:0.##} min em {settings.Threads} threads ..."));

        while (true)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(wallStart);
            if (elapsed >= duration)
            {
                break;
            }

            master.Scan();
            if (elapsed >= nextProgress)
            {
                process.Refresh();
                double cpu = (process.TotalProcessorTime - cpuStart).TotalSeconds / elapsed.TotalSeconds;
                LoopMetrics partial = loop.GetMetrics();
                stdout.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  t = {elapsed.TotalSeconds,5:0} s  deriva máx. = {MaxAbsDrift(plants) * 100:0.0000} %  atraso p99 = {partial.StepLateness.P99Ms:0.00} ms  CPU = {cpu * 100:0.0} % de um núcleo"));
                nextProgress += progressEvery;
            }

            Thread.Sleep(MasterPeriod);
        }

        TimeSpan wall = Stopwatch.GetElapsedTime(wallStart);
        process.Refresh();
        double cpuOfOneCore = (process.TotalProcessorTime - cpuStart).TotalSeconds / wall.TotalSeconds;
        LoopMetrics metrics = loop.GetMetrics();
        RealTimeTracking[] tracking = [.. plants.Select(p => p.Tracking)];
        double maxDrift = tracking.Max(t => Math.Abs(t.Drift));

        return new LoadReport(
            DateTimeOffset.Now,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount,
            definition.Id,
            plants.Length,
            settings.Threads,
            metrics.WaiterKind,
            wall.TotalSeconds,
            metrics.Steps,
            tracking.Min(t => t.Steps),
            tracking.Max(t => t.Steps),
            maxDrift,
            metrics.StepLateness,
            metrics.StepCost,
            metrics.LateCycles,
            metrics.DroppedCycles,
            metrics.Overruns,
            cpuOfOneCore,
            process.WorkingSet64 / (1024.0 * 1024.0),
            master.Scans,
            maxDrift < MaxDrift,
            metrics.StepLateness.P99Ms < MaxP99LatenessMs,
            cpuOfOneCore < MaxCpuOfOneCore);
    }

    private static double MaxAbsDrift(PlantInstance[] plants) => plants.Max(p => Math.Abs(p.Tracking.Drift));

    private static void Print(LoadReport r, TextWriter stdout)
    {
        string Mark(bool ok) => ok ? "OK" : "FALHOU";
        stdout.WriteLine();
        stdout.WriteLine($"Máquina: {r.Machine}, {r.ProcessorCount} processadores lógicos, {r.Os}, {r.Runtime}");
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Plantas: {r.Plants} de '{r.Scenario}' em {r.Threads} threads; espera: {r.WaiterKind}"));
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Duração: {r.WallSeconds:0.0} s de relógio; {r.Steps} passos ({r.MinStepsPerPlant} a {r.MaxStepsPerPlant} por planta); {r.MasterScans} varreduras do mestre"));
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Atraso do passo: média {r.Lateness.MeanMs:0.000} ms, p50 {r.Lateness.P50Ms:0.00} ms, p99 {r.Lateness.P99Ms:0.00} ms, máx. {r.Lateness.MaxMs:0.00} ms"));
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Custo do passo: média {r.StepCost.MeanMs * 1000:0.0} µs, p99 {r.StepCost.P99Ms * 1000:0} µs, máx. {r.StepCost.MaxMs:0.00} ms"));
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Ciclos atrasados: {r.LateCycles}; descartados: {r.DroppedCycles}; estouros: {r.Overruns}; memória: {r.WorkingSetMb:0} MB"));
        stdout.WriteLine();
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{Mark(r.DriftMet)}] deriva máxima {r.MaxDrift * 100:0.0000} % (meta < {MaxDrift * 100:0.0} %)"));
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{Mark(r.LatenessMet)}] atraso do passo p99 {r.Lateness.P99Ms:0.00} ms (meta < {MaxP99LatenessMs:0} ms)"));
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{Mark(r.CpuMet)}] CPU do processo {r.CpuOfOneCore * 100:0.0} % de um núcleo (meta < {MaxCpuOfOneCore * 100:0} %)"));
    }

    private static bool TryParse(string[] args, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Settings? settings, out string? error)
    {
        settings = null;
        if (!CommandArgs.TryParse(args, ["--cenario", "--plantas", "--minutos", "--threads", "--relatorio"], out CommandArgs? parsed, out error))
        {
            return false;
        }

        if (parsed.Positional.Count > 0)
        {
            error = $"Argumento a mais: '{parsed.Positional[0]}'.";
            return false;
        }

        if (parsed.Get("--cenario") is not string scenario)
        {
            error = "Falta --cenario.";
            return false;
        }

        if (!TryInt(parsed.Get("--plantas"), 30, out int plants) || plants < 1)
        {
            error = "--plantas: informe um inteiro maior que zero.";
            return false;
        }

        if (!TryInt(parsed.Get("--threads"), 2, out int threads) || threads < 1)
        {
            error = "--threads: informe um inteiro maior que zero.";
            return false;
        }

        string minutesText = parsed.Get("--minutos") ?? "10";
        if (!double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) || !double.IsFinite(minutes) || minutes <= 0.0)
        {
            error = "--minutos: informe um número maior que zero.";
            return false;
        }

        settings = new Settings(scenario, plants, minutes, threads, parsed.Get("--relatorio"));
        return true;
    }

    private static bool TryInt(string? text, int fallback, out int value)
    {
        if (text is null)
        {
            value = fallback;
            return true;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private sealed record Settings(string ScenarioPath, int Plants, double Minutes, int Threads, string? ReportPath);

    /// <summary>Mestre de teste: lê nível e pressão e escreve as válvulas e o heartbeat de cada planta.</summary>
    private sealed class Master(PlantInstance[] plants)
    {
        private const double Gain = 1.0;
        private readonly ushort[] _measurements = new ushort[2];
        private ushort _heartbeat;

        public long Scans { get; private set; }

        public void Scan()
        {
            _heartbeat++;
            foreach (PlantInstance plant in plants)
            {
                IoImage image = plant.Io.Image;
                if (image.InputRegisterCount < 2 || image.HoldingRegisterCount < 2 || !image.TryReadInputRegisters(0, _measurements))
                {
                    continue;
                }

                // Pontos de ajuste no meio da faixa (0–27648): nível em 50 %, pressão em 62,5 % (10 bar de 16).
                short level = AnalogCodec.FromRegister(_measurements[0]);
                short pressure = AnalogCodec.FromRegister(_measurements[1]);
                image.TryWriteHoldingRegister(0, Command(22748 + (Gain * (13824 - level))));
                image.TryWriteHoldingRegister(1, Command(13824 + (Gain * (17280 - pressure))));
                if (image.HoldingRegisterCount > 10)
                {
                    image.TryWriteHoldingRegister(10, _heartbeat);
                }
            }

            Scans++;
        }

        private static ushort Command(double counts) => AnalogCodec.ToRegister((short)Math.Clamp(Math.Round(counts), 0, 27648));
    }
}

/// <summary>Relatório de <c>simrun carga</c>, gravado em JSON.</summary>
internal sealed record LoadReport(
    DateTimeOffset StartedAt,
    string Machine,
    string Os,
    string Runtime,
    int ProcessorCount,
    string Scenario,
    int Plants,
    int Threads,
    string WaiterKind,
    double WallSeconds,
    long Steps,
    long MinStepsPerPlant,
    long MaxStepsPerPlant,
    double MaxDrift,
    DurationSummary Lateness,
    DurationSummary StepCost,
    long LateCycles,
    long DroppedCycles,
    long Overruns,
    double CpuOfOneCore,
    double WorkingSetMb,
    long MasterScans,
    bool DriftMet,
    bool LatenessMet,
    bool CpuMet)
{
    public bool AllMet => DriftMet && LatenessMet && CpuMet;
}
