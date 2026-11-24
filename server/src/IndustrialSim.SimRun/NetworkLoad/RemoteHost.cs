using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace IndustrialSim.SimRun.NetworkLoad;

/// <summary>
/// A VM do servidor vista pelo <c>ssh</c> do sistema (OpenSSH, com chave e sem senha: <c>BatchMode</c>):
/// reinicia o serviço entre os patamares do teste de carga e amostra CPU e RAM com
/// <c>deploy/scripts/sample-resources.sh</c>, embutido no simrun.
/// </summary>
/// <remarks>
/// Reiniciar pede <c>sudo -n systemctl restart</c> sem senha para esse único comando (regra do sudoers em
/// <c>deploy/</c>). Reiniciar zera as métricas do laço, que são acumuladas desde a partida do processo.
/// </remarks>
internal sealed class RemoteHost(string target, string serviceName)
{
    private const string SamplerResource = "sample-resources.sh";

    public string Target { get; } = target;

    public string ServiceName { get; } = serviceName;

    /// <summary>Reinicia o serviço; devolve a mensagem de erro, ou null se deu certo.</summary>
    public async Task<string?> RestartServiceAsync(CancellationToken cancellationToken)
    {
        (int exitCode, string output) = await RunAsync($"sudo -n systemctl restart {ServiceName}", cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? null : $"ssh {Target}: 'systemctl restart {ServiceName}' saiu com {exitCode}: {output.Trim()}";
    }

    /// <summary>Começa a amostrar a cada <paramref name="interval"/>; as amostras vão para <paramref name="stats"/>.</summary>
    public RemoteSampler StartSampler(TimeSpan interval, ResourceStats stats)
    {
        string script = ReadSamplerScript();
        string seconds = Math.Max(1, (int)Math.Round(interval.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        Process process = Start($"sh -s -- {seconds} {ServiceName}", redirectInput: true);
        process.OutputDataReceived += (_, e) =>
        {
            if (ResourceSample.TryParse(e.Data, out ResourceSample? sample))
            {
                stats.Add(sample);
            }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // O sh lê o script inteiro pela entrada; fechar a entrada não para o laço do script.
        process.StandardInput.Write(script.Replace("\r\n", "\n", StringComparison.Ordinal));
        process.StandardInput.Close();
        return new RemoteSampler(process);
    }

    internal static string ReadSamplerScript()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SamplerResource)
            ?? throw new InvalidOperationException($"Recurso '{SamplerResource}' não embutido no simrun.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string command, CancellationToken cancellationToken)
    {
        using Process process = Start(command, redirectInput: false);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
    }

    private Process Start(string remoteCommand, bool redirectInput)
    {
        var info = new ProcessStartInfo("ssh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Sem senha nem pergunta interativa: o teste roda sozinho por 30 minutos ou mais.
        foreach (string arg in new[] { "-o", "BatchMode=yes", "-o", "ServerAliveInterval=15", "-o", "ConnectTimeout=10", Target, remoteCommand })
        {
            info.ArgumentList.Add(arg);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o ssh.");
    }
}

/// <summary>Amostragem em andamento; parar mata o ssh (e, com ele, o script na VM).</summary>
internal sealed class RemoteSampler(Process process) : IDisposable
{
    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Já terminou.
        }

        process.Dispose();
    }
}
