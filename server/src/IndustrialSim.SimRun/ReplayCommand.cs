using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialSim.Core.Scenarios;
using IndustrialSim.Sessions.Recording;

namespace IndustrialSim.SimRun;

/// <summary>
/// <c>simrun reproduzir gravacao.csv --cenario scenario.json --saida copia.csv</c>: reproduz uma gravação de
/// ensaio feita pelo servidor e confere que a cópia é idêntica (SHA-256 dos dois arquivos).
/// </summary>
internal static class ReplayCommand
{
    public const string Name = "reproduzir";

    public const string Usage = "  simrun reproduzir <gravacao.csv> --cenario <scenario.json> --saida <copia.csv>";

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!CommandArgs.TryParse(args, ["--cenario", "--saida"], out CommandArgs? parsed, out string? error)
            || parsed.Positional.Count != 1
            || parsed.Get("--cenario") is not string scenarioPath
            || parsed.Get("--saida") is not string outputPath)
        {
            stderr.WriteLine(error ?? "Uso:");
            stderr.WriteLine(Usage);
            return SimRunApp.ExitUsage;
        }

        string recordingPath = parsed.Positional[0];
        try
        {
            ScenarioDefinition scenario = ScenarioLoader.Load(File.ReadAllText(scenarioPath));
            ReplayResult result;
            using (var reader = new StreamReader(recordingPath))
            using (var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                result = Replayer.Replay(reader, scenario, writer);
            }

            string original = Sha256(recordingPath);
            string copy = Sha256(outputPath);
            stdout.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Reproduzidos {result.Steps} passos ({result.Steps * scenario.StepSeconds:0.###} s); {result.RowsWritten} linhas em {outputPath}."));
            stdout.WriteLine($"SHA-256 da gravação: {original}");
            stdout.WriteLine($"SHA-256 da cópia:    {copy}");
            if (original != copy)
            {
                stdout.WriteLine("Os arquivos são diferentes.");
                return SimRunApp.ExitFailed;
            }

            stdout.WriteLine("Os arquivos são idênticos.");
            return SimRunApp.ExitOk;
        }
        catch (ScenarioValidationException ex)
        {
            stderr.WriteLine($"Cenário inválido: {scenarioPath}");
            stderr.WriteLine(ex.Message);
            return SimRunApp.ExitFailed;
        }
        catch (FormatException ex)
        {
            stderr.WriteLine($"Gravação inválida ({recordingPath}): {ex.Message}");
            return SimRunApp.ExitFailed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Erro de arquivo: {ex.Message}");
            return SimRunApp.ExitFailed;
        }
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

/// <summary>Argumentos de um subcomando: posicionais e opções <c>--nome valor</c>.</summary>
internal sealed class CommandArgs
{
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);

    public List<string> Positional { get; } = [];

    public string? Get(string option) => _options.GetValueOrDefault(option);

    public static bool TryParse(
        string[] args,
        IReadOnlyCollection<string> known,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CommandArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;
        var result = new CommandArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                result.Positional.Add(arg);
                continue;
            }

            if (!known.Contains(arg))
            {
                error = $"Opção desconhecida: {arg}.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                error = $"Falta o valor da opção {arg}.";
                return false;
            }

            result._options[arg] = args[++i];
        }

        parsed = result;
        return true;
    }
}
