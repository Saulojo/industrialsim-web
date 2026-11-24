using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IndustrialSim.Contracts;
using IndustrialSim.SimRun.NetworkLoad;

namespace IndustrialSim.SimRun;

/// <summary>
/// <c>simrun carga-rede --servidor https://vm --plantas 30 --minutos 30 --ssh admin@vm</c>: teste de carga do
/// servidor implantado, pela rede (Etapa 9A). Cria as plantas pela API, autoriza o IP desta máquina como mestre,
/// roda um mestre Modbus TCP por planta e confere as metas de tempo real, erros, CPU e RAM. Com uma lista em
/// <c>--plantas</c> (ex.: <c>30,60,100,200</c>), faz o teste crescente: um patamar por número, até um falhar.
/// </summary>
internal static class NetworkLoadCommand
{
    public const string Name = "carga-rede";

    public const string Usage =
        "  simrun carga-rede --servidor <https://host> [--plantas 30 | 30,60,100,200] [--minutos 30] [--cenario caldeira]\n" +
        "                    [--ssh <usuario@host>] [--servico industrialsim] [--ca <raiz.crt>] [--ip-mestre <ip>] [--relatorio <rel.json>]";

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryParse(args, out Settings? settings, out string? error))
        {
            stderr.WriteLine(error ?? "Uso:");
            stderr.WriteLine(Usage);
            return SimRunApp.ExitUsage;
        }

        IPAddress? modbusHost = ResolveHost(settings.Server);
        if (modbusHost is null)
        {
            stderr.WriteLine($"Não foi possível resolver o endereço de '{settings.Server.Host}'.");
            return SimRunApp.ExitFailed;
        }

        IPAddress master = settings.MasterAddress ?? LocalAddressTowards(modbusHost);
        using HttpClient http = LoadServerClient.CreateHttpClient(settings.Server, settings.CaPath);
        RemoteHost? remote = settings.SshTarget is null ? null : new RemoteHost(settings.SshTarget, settings.ServiceName);
        if (remote is null)
        {
            stdout.WriteLine("Aviso: sem --ssh, o serviço não é reiniciado entre patamares (as métricas do laço acumulam desde a partida) e a CPU e a RAM da VM não são registradas.");
        }

        var runner = new NetworkLoadRunner(new LoadServerClient(http), modbusHost, master, remote, stdout);
        NetworkLoadReport report = RunAsync(runner, http, settings, modbusHost, master, stdout).GetAwaiter().GetResult();
        Print(report, stdout);
        if (settings.ReportPath is not null)
        {
            File.WriteAllText(settings.ReportPath, JsonSerializer.Serialize(report, ReportJson), new UTF8Encoding(false));
            stdout.WriteLine($"Relatório gravado em {settings.ReportPath}.");
        }

        return report.Phases.All(p => p.Passed) ? SimRunApp.ExitOk : SimRunApp.ExitFailed;
    }

    internal static async Task<NetworkLoadReport> RunAsync(
        NetworkLoadRunner runner,
        HttpClient http,
        Settings settings,
        IPAddress modbusHost,
        IPAddress master,
        TextWriter stdout,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        HealthResponse? health = await new LoadServerClient(http).TryGetHealthAsync(cancellationToken).ConfigureAwait(false);
        stdout.WriteLine($"Servidor {settings.Server} (versão {health?.Version ?? "?"}); Modbus em {modbusHost}; mestre {master}.");

        var phases = new List<PhaseReport>();
        foreach (int plants in settings.PlantSteps)
        {
            PhaseReport phase = await runner.RunPhaseAsync(plants, settings.Scenario, TimeSpan.FromMinutes(settings.MinutesPerStep), cancellationToken).ConfigureAwait(false);
            phases.Add(phase);
            PrintPhase(phase, stdout);
            if (!phase.Passed)
            {
                break;
            }
        }

        PhaseReport? firstFailed = phases.FirstOrDefault(p => !p.Passed);
        int? limit = phases.TakeWhile(p => p.Passed).Select(p => (int?)p.Plants).LastOrDefault();
        return new NetworkLoadReport(
            started,
            settings.Server.ToString(),
            health?.Version,
            settings.Scenario,
            settings.MinutesPerStep,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            master.ToString(),
            runner.Goals,
            phases,
            limit,
            firstFailed?.Failures);
    }

    private static void PrintPhase(PhaseReport p, TextWriter stdout)
    {
        string Line(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
        stdout.WriteLine();
        stdout.WriteLine(Line($"== {p.Plants} plantas: {(p.Passed ? "OK" : "FALHOU")} =="));
        if (p.CreateError is not null)
        {
            stdout.WriteLine(Line($"Interrompido com {p.PlantsCreated} plantas criadas: {p.CreateError}"));
            return;
        }

        if (p.Loop is { } loop)
        {
            stdout.WriteLine(Line($"Laço: {loop.ThreadCount} threads, espera {loop.WaiterKind}; {loop.Steps} passos; atrasados {loop.LateCycles}, descartados {loop.DroppedCycles}, estouros {loop.Overruns}"));
            stdout.WriteLine(Line($"Atraso do passo: média {loop.StepLateness.MeanMs:0.000} ms, p50 {loop.StepLateness.P50Ms:0.00} ms, p99 {loop.StepLateness.P99Ms:0.00} ms, máx. {loop.StepLateness.MaxMs:0.00} ms"));
            stdout.WriteLine(Line($"Custo do passo: média {loop.StepCost.MeanMs * 1000:0.0} µs, p99 {loop.StepCost.P99Ms * 1000:0} µs, máx. {loop.StepCost.MaxMs:0.00} ms"));
        }

        stdout.WriteLine(Line($"Deriva máxima: {p.MaxDrift * 100:0.0000} %; plantas em falha: {p.PlantFaults}"));
        stdout.WriteLine(Line($"Modbus: {p.ModbusScans} varreduras, {p.ModbusErrors} erros, {p.ModbusReconnects} reconexões; varredura p50 {p.ModbusScanMs.P50:0.0} ms, p99 {p.ModbusScanMs.P99:0.0} ms, máx. {p.ModbusScanMs.Max:0.0} ms"));
        if (p.LastModbusError is not null)
        {
            stdout.WriteLine($"Último erro Modbus: {p.LastModbusError}");
        }

        if (p.Resources is { } r)
        {
            stdout.WriteLine(Line($"VM ({r.VmCpus} núcleos, {r.MemTotalMb:0} MB): CPU média {r.VmCpuMean * 100:0.0} %, máx. {r.VmCpuMax * 100:0.0} %; RAM em uso máx. {r.MemUsedMaxMb:0} MB, disponível mín. {r.MemAvailableMinFraction * 100:0.0} %"));
            stdout.WriteLine(Line($"Serviço: CPU média {r.ServiceCoresMean * 100:0.0} % de um núcleo, máx. {r.ServiceCoresMax * 100:0.0} %; memória residente máx. {r.ServiceRssMaxMb:0} MB ({r.Samples} amostras)"));
        }

        stdout.WriteLine(Line($"Esta máquina (mestres): {p.ToolCores * 100:0.0} % de um núcleo; erros HTTP: {p.HttpErrors}"));
        foreach (string failure in p.Failures)
        {
            stdout.WriteLine($"  [FALHOU] {failure}");
        }
    }

    private static void Print(NetworkLoadReport report, TextWriter stdout)
    {
        stdout.WriteLine();
        stdout.WriteLine($"Máquina dos mestres: {report.ToolMachine}, {report.ToolProcessorCount} processadores lógicos, {report.ToolOs}");
        if (report.Phases.Count > 1 || report.Bottleneck is not null)
        {
            stdout.WriteLine(report.LastPassedPlants is int limit
                ? $"Maior patamar dentro das metas: {limit} plantas."
                : "Nenhum patamar dentro das metas.");
            if (report.Bottleneck is not null)
            {
                stdout.WriteLine($"Gargalo: {string.Join("; ", report.Bottleneck)}.");
            }
        }
    }

    internal static bool TryParse(string[] args, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Settings? settings, out string? error)
    {
        settings = null;
        string[] known = ["--servidor", "--plantas", "--minutos", "--cenario", "--ssh", "--servico", "--ca", "--ip-mestre", "--relatorio"];
        if (!CommandArgs.TryParse(args, known, out CommandArgs? parsed, out error))
        {
            return false;
        }

        if (parsed.Positional.Count > 0)
        {
            error = $"Argumento a mais: '{parsed.Positional[0]}'.";
            return false;
        }

        if (parsed.Get("--servidor") is not string serverText
            || !Uri.TryCreate(serverText, UriKind.Absolute, out Uri? server)
            || (server.Scheme != Uri.UriSchemeHttps && server.Scheme != Uri.UriSchemeHttp))
        {
            error = "--servidor: informe o endereço do site, ex.: https://10.0.67.80.";
            return false;
        }

        if (!TryParseSteps(parsed.Get("--plantas") ?? "30", out int[]? steps))
        {
            error = "--plantas: informe um inteiro maior que zero ou uma lista crescente, ex.: 30,60,100,200.";
            return false;
        }

        string minutesText = parsed.Get("--minutos") ?? "30";
        if (!double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) || !double.IsFinite(minutes) || minutes <= 0.0)
        {
            error = "--minutos: informe um número maior que zero.";
            return false;
        }

        IPAddress? master = null;
        if (parsed.Get("--ip-mestre") is string masterText && !IPAddress.TryParse(masterText, out master))
        {
            error = "--ip-mestre: informe um IP.";
            return false;
        }

        string? ca = parsed.Get("--ca");
        if (ca is not null && !File.Exists(ca))
        {
            error = $"--ca: arquivo não encontrado: {ca}.";
            return false;
        }

        settings = new Settings(
            server,
            steps,
            minutes,
            parsed.Get("--cenario") ?? "caldeira",
            parsed.Get("--ssh"),
            parsed.Get("--servico") ?? "industrialsim",
            ca,
            master,
            parsed.Get("--relatorio"));
        return true;
    }

    internal static bool TryParseSteps(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out int[]? steps)
    {
        steps = null;
        var values = new List<int>();
        foreach (string part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1 || (values.Count > 0 && value <= values[^1]))
            {
                return false;
            }

            values.Add(value);
        }

        steps = [.. values];
        return steps.Length > 0;
    }

    private static IPAddress? ResolveHost(Uri server)
    {
        if (IPAddress.TryParse(server.Host.Trim('[', ']'), out IPAddress? address))
        {
            return address;
        }

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(server.DnsSafeHost);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>IP desta máquina na rota até o servidor: é o que o servidor vê como IP do mestre.</summary>
    internal static IPAddress LocalAddressTowards(IPAddress remote)
    {
        // Um socket UDP "conectado" só escolhe a rota; nenhum pacote é enviado.
        using var socket = new Socket(remote.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect(new IPEndPoint(remote, 9));
        return ((IPEndPoint)socket.LocalEndPoint!).Address;
    }

    internal sealed record Settings(
        Uri Server,
        int[] PlantSteps,
        double MinutesPerStep,
        string Scenario,
        string? SshTarget,
        string ServiceName,
        string? CaPath,
        IPAddress? MasterAddress,
        string? ReportPath);
}

/// <summary>Relatório de <c>simrun carga-rede</c>, gravado em JSON.</summary>
internal sealed record NetworkLoadReport(
    DateTimeOffset StartedAt,
    string Server,
    string? ServerVersion,
    string Scenario,
    double MinutesPerStep,
    string ToolMachine,
    string ToolOs,
    int ToolProcessorCount,
    string MasterAddress,
    LoadGoals Goals,
    IReadOnlyList<PhaseReport> Phases,
    int? LastPassedPlants,
    IReadOnlyList<string>? Bottleneck);
