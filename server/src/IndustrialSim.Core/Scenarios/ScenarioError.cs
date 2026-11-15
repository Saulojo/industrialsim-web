namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Um erro de validação de cenário, com o caminho do campo no JSON (ex.: <c>components[2].params.areaM2</c>).
/// O caminho <c>$</c> é o documento inteiro.
/// </summary>
public sealed record ScenarioError(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>Cenário inválido. Traz todos os erros encontrados, cada um com o caminho do campo.</summary>
public sealed class ScenarioValidationException : Exception
{
    public ScenarioValidationException(IReadOnlyList<ScenarioError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<ScenarioError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<ScenarioError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        string header = errors.Count == 1 ? "Cenário inválido (1 erro):" : $"Cenário inválido ({errors.Count} erros):";
        return header + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  " + e));
    }
}
