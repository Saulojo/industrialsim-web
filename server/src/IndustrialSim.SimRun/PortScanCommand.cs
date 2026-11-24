using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace IndustrialSim.SimRun;

/// <summary>
/// <c>simrun portas --host 10.0.67.80 --esperadas 22,443,15000-15999</c>: varredura de portas TCP feita de fora da
/// VM (Etapa 9A; RNF06). Tenta conectar em cada porta da faixa, com várias conexões ao mesmo tempo, e lista as que
/// aceitaram. Passa se todas as abertas estão entre as esperadas.
/// </summary>
/// <remarks>
/// O critério é só a lista de portas abertas. Porta descartada pelo firewall termina pelo tempo-limite; porta liberada
/// mas sem programa (ex.: uma porta da faixa Modbus sem planta) é recusada, mas no Windows a recusa pode passar do
/// tempo-limite (o cliente repete o SYN), então "recusada" e "sem resposta" são só informativos. UDP não é varrido: o
/// firewall da VM não libera nenhuma porta UDP, e isso fica conferido no <c>ufw status</c> das evidências.
/// </remarks>
internal static class PortScanCommand
{
    public const string Name = "portas";

    public const string Usage =
        "  simrun portas --host <ip> [--faixa 1-65535] [--esperadas 22,443,15000-15999] [--paralelas 512] [--tempo-limite-ms 400] [--relatorio <rel.json>]";

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryParse(args, out Settings? settings, out string? error))
        {
            stderr.WriteLine(error ?? "Uso:");
            stderr.WriteLine(Usage);
            return SimRunApp.ExitUsage;
        }

        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Varrendo {settings.Host}, portas TCP {settings.First} a {settings.Last}, {settings.Parallel} conexões ao mesmo tempo, tempo-limite de {settings.TimeoutMs} ms ..."));
        DateTimeOffset started = DateTimeOffset.UtcNow;
        ScanResult result = ScanAsync(settings.Host, settings.First, settings.Last, settings.Parallel, TimeSpan.FromMilliseconds(settings.TimeoutMs)).GetAwaiter().GetResult();

        int[] unexpected = [.. result.Open.Where(p => !settings.Expected.Contains(p))];
        var report = new PortScanReport(
            started,
            settings.Host.ToString(),
            settings.First,
            settings.Last,
            settings.TimeoutMs,
            Describe(settings.Expected),
            result.Open,
            result.Refused,
            result.TimedOut,
            unexpected);

        stdout.WriteLine($"Abertas: {(result.Open.Count == 0 ? "nenhuma" : string.Join(", ", result.Open))}");
        stdout.WriteLine($"Recusaram na hora (sem programa ouvindo): {Describe(result.RefusedPorts)}");
        stdout.WriteLine($"Sem resposta no tempo-limite (descartadas pelo firewall ou recusa lenta): {result.TimedOut}");
        stdout.WriteLine(unexpected.Length == 0
            ? $"[OK] só portas esperadas abertas ({Describe(settings.Expected)})"
            : $"[FALHOU] portas abertas fora das esperadas: {string.Join(", ", unexpected)}");

        if (settings.ReportPath is not null)
        {
            File.WriteAllText(settings.ReportPath, JsonSerializer.Serialize(report, ReportJson), new UTF8Encoding(false));
            stdout.WriteLine($"Relatório gravado em {settings.ReportPath}.");
        }

        return unexpected.Length == 0 ? SimRunApp.ExitOk : SimRunApp.ExitFailed;
    }

    internal static async Task<ScanResult> ScanAsync(IPAddress host, int first, int last, int parallel, TimeSpan timeout)
    {
        var open = new List<int>();
        var refused = new List<int>();
        int timedOut = 0;
        var gate = new Lock();
        using var slots = new SemaphoreSlim(parallel);

        async Task Probe(int port)
        {
            await slots.WaitAsync().ConfigureAwait(false);
            try
            {
                PortState state = await ProbeAsync(host, port, timeout).ConfigureAwait(false);
                lock (gate)
                {
                    switch (state)
                    {
                        case PortState.Open:
                            open.Add(port);
                            break;
                        case PortState.Refused:
                            refused.Add(port);
                            break;
                        default:
                            timedOut++;
                            break;
                    }
                }
            }
            finally
            {
                slots.Release();
            }
        }

        await Task.WhenAll(Enumerable.Range(first, last - first + 1).Select(Probe)).ConfigureAwait(false);
        open.Sort();
        refused.Sort();
        return new ScanResult(open, refused.Count, timedOut, refused);
    }

    private static async Task<PortState> ProbeAsync(IPAddress host, int port, TimeSpan timeout)
    {
        using var socket = new Socket(host.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(host, port), cancel.Token).ConfigureAwait(false);
            return PortState.Open;
        }
        catch (OperationCanceledException)
        {
            return PortState.TimedOut;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return PortState.Refused;
        }
        catch (SocketException)
        {
            // Rede inalcançável, host inalcançável etc.: tratado como sem resposta.
            return PortState.TimedOut;
        }
    }

    internal static bool TryParse(string[] args, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Settings? settings, out string? error)
    {
        settings = null;
        string[] known = ["--host", "--faixa", "--esperadas", "--paralelas", "--tempo-limite-ms", "--relatorio"];
        if (!CommandArgs.TryParse(args, known, out CommandArgs? parsed, out error))
        {
            return false;
        }

        if (parsed.Positional.Count > 0)
        {
            error = $"Argumento a mais: '{parsed.Positional[0]}'.";
            return false;
        }

        if (parsed.Get("--host") is not string hostText || !IPAddress.TryParse(hostText, out IPAddress? host))
        {
            error = "--host: informe o IP da VM.";
            return false;
        }

        if (!TryParsePorts(parsed.Get("--faixa") ?? "1-65535", out HashSet<int>? range) || range.Count == 0)
        {
            error = "--faixa: informe uma faixa, ex.: 1-65535.";
            return false;
        }

        int first = range.Min();
        int last = range.Max();
        if (range.Count != last - first + 1)
        {
            error = "--faixa: informe uma única faixa contínua, ex.: 1-65535.";
            return false;
        }

        if (!TryParsePorts(parsed.Get("--esperadas") ?? "22,443,15000-15999", out HashSet<int>? expected))
        {
            error = "--esperadas: informe portas e faixas separadas por vírgula, ex.: 22,443,15000-15999.";
            return false;
        }

        if (!TryPositive(parsed.Get("--paralelas"), 512, out int parallel) || !TryPositive(parsed.Get("--tempo-limite-ms"), 400, out int timeoutMs))
        {
            error = "--paralelas e --tempo-limite-ms: informe inteiros maiores que zero.";
            return false;
        }

        settings = new Settings(host, first, last, expected, parallel, timeoutMs, parsed.Get("--relatorio"));
        return true;
    }

    /// <summary>Lê <c>22,443,15000-15999</c>; vazio vale como nenhuma porta.</summary>
    internal static bool TryParsePorts(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out HashSet<int>? ports)
    {
        ports = [];
        foreach (string part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] bounds = part.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length is < 1 or > 2
                || !TryPort(bounds[0], out int from)
                || !TryPort(bounds[^1], out int to)
                || to < from)
            {
                ports = null;
                return false;
            }

            for (int port = from; port <= to; port++)
            {
                ports.Add(port);
            }
        }

        return true;
    }

    /// <summary>Escreve as portas como faixas: <c>22, 443, 15000-15999</c>.</summary>
    internal static string Describe(IEnumerable<int> ports)
    {
        int[] sorted = [.. ports.Distinct().Order()];
        if (sorted.Length == 0)
        {
            return "nenhuma";
        }

        var parts = new List<string>();
        int start = sorted[0];
        int previous = start;
        foreach (int port in sorted.Skip(1).Append(int.MinValue))
        {
            if (port == previous + 1)
            {
                previous = port;
                continue;
            }

            parts.Add(start == previous ? start.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{start}-{previous}"));
            start = previous = port;
        }

        return string.Join(", ", parts);
    }

    private static bool TryPort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    private static bool TryPositive(string? text, int fallback, out int value)
    {
        if (text is null)
        {
            value = fallback;
            return true;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    internal enum PortState
    {
        Open,
        Refused,
        TimedOut,
    }

    internal sealed record ScanResult(IReadOnlyList<int> Open, int Refused, int TimedOut, IReadOnlyList<int> RefusedPorts);

    internal sealed record Settings(IPAddress Host, int First, int Last, HashSet<int> Expected, int Parallel, int TimeoutMs, string? ReportPath);
}

/// <summary>Relatório de <c>simrun portas</c>, gravado em JSON.</summary>
internal sealed record PortScanReport(
    DateTimeOffset StartedAt,
    string Host,
    int First,
    int Last,
    int TimeoutMs,
    string Expected,
    IReadOnlyList<int> Open,
    int Refused,
    int TimedOut,
    IReadOnlyList<int> Unexpected);
