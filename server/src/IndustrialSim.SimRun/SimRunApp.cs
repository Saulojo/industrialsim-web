using System.Globalization;
using System.Text;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.SimRun;

/// <summary>
/// Linha de comando <c>simrun</c>: roda um cenário sem servidor, para testes e validação.
/// É ferramenta de desenvolvimento; as mensagens ficam em português, como as do núcleo.
/// </summary>
public static class SimRunApp
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;

    private const string Usage = """
        Uso:
          simrun <cenario.json> --saida <resultado.csv> [--entradas <roteiro.csv>] [--duracao <s>] [--intervalo <s>] [--semente <n>]
          simrun <cenario.json>                 confere o cenário e mostra entradas, saídas e ordem de execução
          simrun --esquema <arquivo.json>       grava o esquema JSON do scenario.json
          simrun reproduzir <gravacao.csv> --cenario <scenario.json> --saida <copia.csv>
                                                reproduz uma gravação de ensaio do servidor e compara os SHA-256
          simrun carga --cenario <scenario.json> [--plantas 30] [--minutos 10] [--threads 2] [--relatorio <rel.json>]
                                                ensaio de carga em tempo real: deriva, atraso do passo e CPU

        Opções:
          --entradas   roteiro CSV: coluna 'time' (s) e uma coluna por entrada do cenário; cada linha vale a partir do seu instante
          --saida      CSV de resultado: time, entradas e saídas do cenário
          --duracao    tempo simulado, em segundos (padrão: o instante da última linha do roteiro)
          --intervalo  intervalo entre linhas do resultado, em segundos (padrão: o passo do cenário)
          --semente    semente do ruído, no lugar da que está no cenário
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length > 0 && args[0] == ReplayCommand.Name)
        {
            return ReplayCommand.Run(args[1..], stdout, stderr);
        }

        if (args.Length > 0 && args[0] == LoadCommand.Name)
        {
            return LoadCommand.Run(args[1..], stdout, stderr);
        }

        if (!Options.TryParse(args, out Options? options, out string? usageError))
        {
            stderr.WriteLine(usageError);
            stderr.WriteLine();
            stderr.WriteLine(Usage);
            return ExitUsage;
        }

        try
        {
            if (options.SchemaPath is not null)
            {
                File.WriteAllText(options.SchemaPath, ScenarioSchemaWriter.Write(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                stdout.WriteLine($"Esquema gravado em {options.SchemaPath}.");
                return ExitOk;
            }

            ScenarioPlant plant = ScenarioPlant.FromJson(File.ReadAllText(options.ScenarioPath!), noiseSeed: options.NoiseSeed);
            Describe(plant, stdout);

            if (options.OutputPath is null)
            {
                return ExitOk;
            }

            InputScript script = InputScript.Empty;
            if (options.InputsPath is not null)
            {
                using var reader = new StreamReader(options.InputsPath);
                script = InputScript.Parse(reader);
            }

            double duration = options.DurationSeconds ?? script.LastTimeSeconds;
            if (duration <= 0.0)
            {
                stderr.WriteLine("Sem duração: informe --duracao, ou um roteiro cuja última linha marque o fim do ensaio.");
                return ExitUsage;
            }

            double step = plant.Clock.StepSeconds;
            int every = options.IntervalSeconds is double interval ? Math.Max(1, (int)Math.Round(interval / step)) : 1;

            RunResult result;
            using (var writer = new StreamWriter(options.OutputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                result = SimulationRunner.Run(plant, script, writer, duration, every);
            }

            stdout.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Simulados {result.Steps} passos ({result.Steps * step:0.###} s); {result.RowsWritten} linhas em {options.OutputPath}."));
            stdout.WriteLine($"Hash do estado (acumulado em todos os passos): {result.StateHash:x16}");
            return ExitOk;
        }
        catch (ScenarioValidationException ex)
        {
            stderr.WriteLine($"Cenário inválido: {options.ScenarioPath}");
            foreach (ScenarioError error in ex.Errors)
            {
                stderr.WriteLine($"  {error}");
            }

            return ExitFailed;
        }
        catch (FormatException ex)
        {
            stderr.WriteLine($"Roteiro inválido ({options.InputsPath}): {ex.Message}");
            return ExitFailed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Erro de arquivo: {ex.Message}");
            return ExitFailed;
        }
    }

    private static void Describe(ScenarioPlant plant, TextWriter stdout)
    {
        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Cenário '{plant.Definition.Id}': {plant.Components.Count} componentes, passo de {plant.Clock.StepSeconds} s."));
        stdout.WriteLine($"Entradas: {JoinOrNone(plant.InputNames)}");
        stdout.WriteLine($"Saídas: {JoinOrNone(plant.OutputNames)}");
        stdout.WriteLine($"Ordem de execução: {string.Join(" -> ", plant.ExecutionOrderIds)}");
    }

    private static string JoinOrNone(IReadOnlyList<string> names) => names.Count == 0 ? "(nenhuma)" : string.Join(", ", names);

    private sealed class Options
    {
        public string? ScenarioPath { get; private set; }

        public string? InputsPath { get; private set; }

        public string? OutputPath { get; private set; }

        public string? SchemaPath { get; private set; }

        public double? DurationSeconds { get; private set; }

        public double? IntervalSeconds { get; private set; }

        public ulong? NoiseSeed { get; private set; }

        public static bool TryParse(string[] args, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Options? options, out string? error)
        {
            var parsed = new Options();
            options = null;
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    if (parsed.ScenarioPath is not null)
                    {
                        error = $"Argumento a mais: '{arg}'. Só um cenário por execução.";
                        return false;
                    }

                    parsed.ScenarioPath = arg;
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    error = $"Falta o valor da opção {arg}.";
                    return false;
                }

                string value = args[++i];
                switch (arg)
                {
                    case "--entradas":
                        parsed.InputsPath = value;
                        break;
                    case "--saida":
                        parsed.OutputPath = value;
                        break;
                    case "--esquema":
                        parsed.SchemaPath = value;
                        break;
                    case "--duracao":
                        if (!TryPositive(value, out double duration))
                        {
                            error = $"--duracao: '{value}' não é um número de segundos maior que zero.";
                            return false;
                        }

                        parsed.DurationSeconds = duration;
                        break;
                    case "--intervalo":
                        if (!TryPositive(value, out double interval))
                        {
                            error = $"--intervalo: '{value}' não é um número de segundos maior que zero.";
                            return false;
                        }

                        parsed.IntervalSeconds = interval;
                        break;
                    case "--semente":
                        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
                        {
                            error = $"--semente: '{value}' não é um inteiro não negativo.";
                            return false;
                        }

                        parsed.NoiseSeed = seed;
                        break;
                    default:
                        error = $"Opção desconhecida: {arg}.";
                        return false;
                }
            }

            if (parsed.SchemaPath is null && parsed.ScenarioPath is null)
            {
                error = "Falta o arquivo do cenário.";
                return false;
            }

            options = parsed;
            return true;
        }

        private static bool TryPositive(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value > 0.0;
    }
}
