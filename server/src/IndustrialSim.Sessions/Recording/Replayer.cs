using System.Globalization;
using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;

namespace IndustrialSim.Sessions.Recording;

/// <summary>Resultado de uma reprodução.</summary>
/// <param name="Steps">Passos simulados.</param>
/// <param name="RowsWritten">Linhas de dados escritas na saída.</param>
public sealed record ReplayResult(long Steps, long RowsWritten);

/// <summary>
/// Reproduz uma gravação de <see cref="PlantRecorder"/>: monta a planta do cenário com a mesma semente, aplica
/// em cada passo os comandos, as configurações e as entradas gravados e grava de novo, no mesmo formato. Como a
/// planta é determinística (RNF02), a saída é idêntica à gravação original, byte a byte.
/// </summary>
/// <remarks>
/// Entre duas linhas da gravação nada mudou, então os passos intermediários repetem os comandos da linha
/// anterior. As entradas gravadas (em SI) são aplicadas antes do passo: isso refaz também as entradas que não
/// vêm da imagem de I/O (distúrbios); as que vêm dela são recalculadas a partir dos registradores.
/// </remarks>
public static class Replayer
{
    public static ReplayResult Replay(TextReader recording, ScenarioDefinition scenario, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(output);

        (RecordingHeader header, string[] columns, int lineNumber) = ReadHeader(recording);
        if (header.ScenarioId != scenario.Id)
        {
            throw new FormatException($"a gravação é do cenário '{header.ScenarioId}', não de '{scenario.Id}'.");
        }

        if (header.StepSeconds != scenario.StepSeconds)
        {
            throw new FormatException($"a gravação tem passo de {header.StepSeconds} s e o cenário, de {scenario.StepSeconds} s.");
        }

        var io = new PlantIo(ScenarioPlant.Create(scenario, noiseSeed: header.NoiseSeed));
        var recorder = new PlantRecorder(io, header, line =>
        {
            output.Write(line);
            output.Write('\n');
        });

        if (!columns.SequenceEqual(recorder.Columns, StringComparer.Ordinal))
        {
            throw new FormatException("as colunas da gravação não correspondem às do cenário (outra versão do cenário?).");
        }

        var layout = new Layout(io);
        var coils = new bool[layout.CoilCount];
        var holding = new ushort[layout.HoldingCount];
        IoSettings settings = io.Settings;
        string? line;
        while ((line = recording.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(',');
            if (fields.Length != columns.Length)
            {
                throw new FormatException($"linha {lineNumber}: {fields.Length} campos, esperados {columns.Length}.");
            }

            long step = ParseLong(fields[0], lineNumber, "step");
            long current = io.Plant.Clock.StepCount;
            if (step <= current)
            {
                throw new FormatException($"linha {lineNumber}: passo {step} fora de ordem (a planta já está no passo {current}).");
            }

            // Passos sem linha: nada mudou desde a linha anterior.
            while (io.Plant.Clock.StepCount < step - 1)
            {
                StepWith(io, recorder, coils, holding, settings);
            }

            layout.Parse(fields, lineNumber, coils, holding, out settings, io.Plant);
            StepWith(io, recorder, coils, holding, settings);
        }

        return new ReplayResult(io.Plant.Clock.StepCount, recorder.RowsWritten);
    }

    private static void StepWith(PlantIo io, PlantRecorder recorder, bool[] coils, ushort[] holding, IoSettings settings)
    {
        io.StepWithCommands(coils, holding, settings);
        recorder.OnStepCompleted();
    }

    /// <summary>Lê o cabeçalho e a linha de colunas; devolve também quantas linhas foram lidas.</summary>
    private static (RecordingHeader Header, string[] Columns, int LinesRead) ReadHeader(TextReader reader)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        int linesRead = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            linesRead++;
            if (!line.StartsWith('#'))
            {
                break;
            }

            int equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals > 1)
            {
                values[line[1..equals]] = line[(equals + 1)..];
            }
        }

        if (line is null)
        {
            throw new FormatException("gravação sem a linha de colunas.");
        }

        if (!values.TryGetValue("format", out string? format) || format != PlantRecorder.FormatName)
        {
            throw new FormatException($"formato de gravação desconhecido; esperado '{PlantRecorder.FormatName}'.");
        }

        var header = new RecordingHeader(
            Required(values, "scenario"),
            ulong.Parse(Required(values, "noise_seed"), NumberStyles.None, CultureInfo.InvariantCulture),
            double.Parse(Required(values, "step_s"), NumberStyles.Float, CultureInfo.InvariantCulture),
            int.Parse(Required(values, "sample_every_steps"), NumberStyles.None, CultureInfo.InvariantCulture));
        return (header, line.Split(','), linesRead);
    }

    private static string Required(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) ? value : throw new FormatException($"cabeçalho sem '#{key}='.");

    private static long ParseLong(string text, int lineNumber, string column) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new FormatException($"linha {lineNumber}, coluna {column}: '{text}' não é um inteiro.");

    /// <summary>Posição de cada grupo de colunas numa linha.</summary>
    private sealed class Layout(PlantIo io)
    {
        private const int FirstCoil = 2;

        public int CoilCount { get; } = io.Image.CoilCount;

        public int HoldingCount { get; } = io.Image.HoldingRegisterCount;

        public void Parse(string[] fields, int lineNumber, bool[] coils, ushort[] holding, out IoSettings settings, ScenarioPlant plant)
        {
            int index = FirstCoil;
            for (int i = 0; i < coils.Length; i++, index++)
            {
                coils[i] = ParseBool(fields[index], lineNumber, index);
            }

            for (int i = 0; i < holding.Length; i++, index++)
            {
                holding[i] = ushort.TryParse(fields[index], NumberStyles.None, CultureInfo.InvariantCulture, out ushort value)
                    ? value
                    : throw Error(lineNumber, index, fields[index]);
            }

            settings = new IoSettings(
                Enum.TryParse(fields[index], ignoreCase: false, out AnalogFormat format) && Enum.IsDefined(format) ? format : throw Error(lineNumber, index, fields[index]),
                Enum.TryParse(fields[index + 1], ignoreCase: false, out WordOrder order) && Enum.IsDefined(order) ? order : throw Error(lineNumber, index + 1, fields[index + 1]),
                ParseBool(fields[index + 2], lineNumber, index + 2),
                ParseDouble(fields[index + 3], lineNumber, index + 3),
                ParseBool(fields[index + 4], lineNumber, index + 4));
            index += 5;

            for (int i = 0; i < plant.InputNames.Count; i++, index++)
            {
                plant.SetInput(i, ParseDouble(fields[index], lineNumber, index));
            }
        }

        private static bool ParseBool(string text, int lineNumber, int column) => text switch
        {
            "0" => false,
            "1" => true,
            _ => throw Error(lineNumber, column, text),
        };

        private static double ParseDouble(string text, int lineNumber, int column) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : throw Error(lineNumber, column, text);

        private static FormatException Error(int lineNumber, int column, string text) =>
            new($"linha {lineNumber}, campo {column + 1}: valor inválido '{text}'.");
    }
}
