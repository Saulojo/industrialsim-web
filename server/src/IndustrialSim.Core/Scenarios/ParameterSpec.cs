using System.Globalization;

namespace IndustrialSim.Core.Scenarios;

public enum ParameterKind
{
    Number,
    Choice,
}

/// <summary>
/// Descrição de um parâmetro (ou condição inicial) de um tipo de componente: nome, unidade, faixa e valor padrão.
/// É a partir dela que o cenário é validado e que o esquema JSON é gerado.
/// </summary>
public sealed record ParameterSpec
{
    private ParameterSpec(string name, ParameterKind kind, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Kind = kind;
        Description = description;
    }

    public string Name { get; }

    public ParameterKind Kind { get; }

    /// <summary>Texto de ajuda para quem escreve o cenário (vai para as mensagens de erro e para o esquema).</summary>
    public string Description { get; }

    /// <summary>Unidade SI, só para documentação (ex.: <c>m²</c>). Vazio para grandezas sem unidade.</summary>
    public string Unit { get; private init; } = string.Empty;

    /// <summary>Valor padrão; nulo quando o parâmetro numérico é obrigatório.</summary>
    public double? Default { get; private init; }

    public double? Minimum { get; private init; }

    public double? Maximum { get; private init; }

    /// <summary>Se verdadeiro, o próprio mínimo não é aceito (faixa aberta).</summary>
    public bool ExclusiveMinimum { get; private init; }

    public bool ExclusiveMaximum { get; private init; }

    /// <summary>Opções aceitas, para <see cref="ParameterKind.Choice"/>.</summary>
    public IReadOnlyList<string> Choices { get; private init; } = [];

    public string? DefaultChoice { get; private init; }

    public bool IsRequired => Kind == ParameterKind.Number ? Default is null : DefaultChoice is null;

    /// <summary>Parâmetro numérico. Sem <paramref name="defaultValue"/>, é obrigatório.</summary>
    public static ParameterSpec Number(
        string name,
        string description,
        string unit = "",
        double? defaultValue = null,
        double? min = null,
        double? max = null,
        bool exclusiveMin = false,
        bool exclusiveMax = false) =>
        new(name, ParameterKind.Number, description)
        {
            Unit = unit,
            Default = defaultValue,
            Minimum = min,
            Maximum = max,
            ExclusiveMinimum = exclusiveMin,
            ExclusiveMaximum = exclusiveMax,
        };

    /// <summary>Parâmetro de escolha entre textos fixos. Sem <paramref name="defaultChoice"/>, é obrigatório.</summary>
    public static ParameterSpec Choice(string name, string description, IReadOnlyList<string> choices, string? defaultChoice = null) =>
        new(name, ParameterKind.Choice, description) { Choices = choices, DefaultChoice = defaultChoice };

    /// <summary>Faixa aceita em texto (ex.: <c>&gt; 0 e ≤ 1</c>); vazio se não houver limite.</summary>
    public string DescribeRange()
    {
        var parts = new List<string>(2);
        if (Minimum is double min)
        {
            parts.Add((ExclusiveMinimum ? "> " : "≥ ") + min.ToString("R", CultureInfo.InvariantCulture));
        }

        if (Maximum is double max)
        {
            parts.Add((ExclusiveMaximum ? "< " : "≤ ") + max.ToString("R", CultureInfo.InvariantCulture));
        }

        return string.Join(" e ", parts);
    }

    internal bool IsInRange(double value)
    {
        if (Minimum is double min && (ExclusiveMinimum ? value <= min : value < min))
        {
            return false;
        }

        if (Maximum is double max && (ExclusiveMaximum ? value >= max : value > max))
        {
            return false;
        }

        return true;
    }
}

/// <summary>Valores já validados de um grupo de parâmetros, com os padrões aplicados.</summary>
public sealed class ParameterValues
{
    private readonly Dictionary<string, double> _numbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _choices = new(StringComparer.Ordinal);
    private readonly HashSet<string> _given = new(StringComparer.Ordinal);

    private ParameterValues()
    {
    }

    public double Number(string name) => _numbers[name];

    public string Choice(string name) => _choices[name];

    /// <summary>Verdadeiro se o valor veio escrito no cenário (falso se é o padrão).</summary>
    public bool IsGiven(string name) => _given.Contains(name);

    /// <summary>
    /// Confere os valores dados contra as especificações e aplica os padrões. Cada problema vira um erro
    /// com o caminho <paramref name="basePath"/>.nome.
    /// </summary>
    internal static ParameterValues Validate(
        IReadOnlyList<ParameterSpec> specs,
        IReadOnlyDictionary<string, ScenarioValue> given,
        string basePath,
        string typeName,
        List<ScenarioError> errors)
    {
        var values = new ParameterValues();

        foreach (string name in given.Keys)
        {
            if (!specs.Any(s => s.Name == name))
            {
                string accepted = specs.Count == 0 ? "nenhum" : string.Join(", ", specs.Select(s => s.Name));
                errors.Add(new ScenarioError($"{basePath}.{name}", $"campo desconhecido para o tipo '{typeName}'. Aceitos: {accepted}."));
            }
        }

        foreach (ParameterSpec spec in specs)
        {
            string path = $"{basePath}.{spec.Name}";
            string help = spec.Unit.Length == 0 ? spec.Description : $"{spec.Description}, em {spec.Unit}";

            if (!given.TryGetValue(spec.Name, out ScenarioValue value))
            {
                if (spec.IsRequired)
                {
                    errors.Add(new ScenarioError(path, $"campo obrigatório ausente ({help})."));
                }
                else if (spec.Kind == ParameterKind.Number)
                {
                    values._numbers[spec.Name] = spec.Default!.Value;
                }
                else
                {
                    values._choices[spec.Name] = spec.DefaultChoice!;
                }

                continue;
            }

            values._given.Add(spec.Name);
            if (spec.Kind == ParameterKind.Number)
            {
                if (value.Kind != ScenarioValueKind.Number)
                {
                    errors.Add(new ScenarioError(path, $"esperado um número ({help}); recebido {value.Describe()}."));
                }
                else if (!spec.IsInRange(value.Number))
                {
                    errors.Add(new ScenarioError(
                        path,
                        $"valor {value.Number.ToString("R", CultureInfo.InvariantCulture)} fora da faixa: deve ser {spec.DescribeRange()} ({help})."));
                }
                else
                {
                    values._numbers[spec.Name] = value.Number;
                }
            }
            else if (value.Kind != ScenarioValueKind.Text || !spec.Choices.Contains(value.Text!))
            {
                errors.Add(new ScenarioError(path, $"valor inválido: recebido {value.Describe()}. Opções: {string.Join(", ", spec.Choices)}."));
            }
            else
            {
                values._choices[spec.Name] = value.Text!;
            }
        }

        return values;
    }
}
