using System.Globalization;

namespace IndustrialSim.SimRun;

/// <summary>
/// Roteiro de entradas em CSV. A primeira coluna é <c>time</c> (segundos); as demais são nomes de entradas
/// externas do cenário. Cada linha diz: "a partir deste instante, estas entradas valem isto". Célula vazia
/// mantém o valor anterior. Linhas em branco e linhas começadas por <c>#</c> são ignoradas.
/// </summary>
/// <remarks>
/// Separador: vírgula, com ponto decimal; ou ponto e vírgula, caso em que a vírgula decimal também é aceita
/// (é como o Excel em português exporta). O separador é detectado pelo cabeçalho.
/// </remarks>
public sealed class InputScript
{
    public const string TimeColumn = "time";

    private InputScript(IReadOnlyList<string> inputNames, IReadOnlyList<Row> rows)
    {
        InputNames = inputNames;
        Rows = rows;
    }

    /// <summary>Roteiro sem linhas: as entradas ficam nos valores iniciais do cenário.</summary>
    public static InputScript Empty { get; } = new([], []);

    /// <summary>Nomes das entradas, na ordem das colunas (sem a coluna de tempo).</summary>
    public IReadOnlyList<string> InputNames { get; }

    /// <summary>Linhas, em ordem não decrescente de tempo.</summary>
    public IReadOnlyList<Row> Rows { get; }

    /// <summary>Instante da última linha, em segundos (0 se não houver linhas).</summary>
    public double LastTimeSeconds => Rows.Count == 0 ? 0.0 : Rows[^1].TimeSeconds;

    /// <summary>Lê o roteiro; lança <see cref="FormatException"/> com o número da linha do problema.</summary>
    public static InputScript Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string[]? header = null;
        char separator = ',';
        var rows = new List<Row>();
        int lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (header is null)
            {
                separator = line.Contains(';', StringComparison.Ordinal) ? ';' : ',';
                header = [.. line.Split(separator).Select(cell => cell.Trim())];
                ValidateHeader(header, lineNumber);
                continue;
            }

            string[] cells = line.Split(separator);
            if (cells.Length != header.Length)
            {
                throw new FormatException($"linha {lineNumber}: {cells.Length} colunas; o cabeçalho tem {header.Length}.");
            }

            double time = ParseNumber(cells[0], separator, lineNumber, TimeColumn)
                ?? throw new FormatException($"linha {lineNumber}: a coluna '{TimeColumn}' não pode ficar vazia.");
            if (time < 0.0)
            {
                throw new FormatException($"linha {lineNumber}: tempo negativo ({cells[0].Trim()}).");
            }

            if (rows.Count > 0 && time < rows[^1].TimeSeconds)
            {
                throw new FormatException($"linha {lineNumber}: o tempo {cells[0].Trim()} é menor que o da linha anterior; as linhas devem estar em ordem.");
            }

            var values = new double?[header.Length - 1];
            for (int i = 1; i < cells.Length; i++)
            {
                values[i - 1] = ParseNumber(cells[i], separator, lineNumber, header[i]);
            }

            rows.Add(new Row(time, values));
        }

        if (header is null)
        {
            throw new FormatException($"o roteiro está vazio: falta o cabeçalho '{TimeColumn},<entrada>,...'.");
        }

        return new InputScript(header[1..], rows);
    }

    private static void ValidateHeader(string[] header, int lineNumber)
    {
        if (header[0] != TimeColumn)
        {
            throw new FormatException($"linha {lineNumber}: a primeira coluna deve se chamar '{TimeColumn}'; veio '{header[0]}'.");
        }

        for (int i = 1; i < header.Length; i++)
        {
            if (header[i].Length == 0)
            {
                throw new FormatException($"linha {lineNumber}: a coluna {i + 1} está sem nome.");
            }

            if (Array.IndexOf(header, header[i]) != i)
            {
                throw new FormatException($"linha {lineNumber}: a coluna '{header[i]}' aparece duas vezes.");
            }
        }
    }

    private static double? ParseNumber(string cell, char separator, int lineNumber, string column)
    {
        string text = cell.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (separator == ';')
        {
            text = text.Replace(',', '.');
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            throw new FormatException($"linha {lineNumber}, coluna '{column}': '{cell.Trim()}' não é um número.");
        }

        return value;
    }

    /// <summary>Uma linha do roteiro: o instante e o valor de cada entrada (nulo = manter).</summary>
    public sealed record Row(double TimeSeconds, IReadOnlyList<double?> Values);
}
