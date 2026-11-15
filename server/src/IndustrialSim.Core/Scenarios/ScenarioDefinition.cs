using System.Globalization;

namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Conteúdo de um <c>scenario.json</c> já lido e com a estrutura conferida (D04). Ainda não é uma planta:
/// tipos, parâmetros e ligações são conferidos quando <see cref="ScenarioPlant.Create"/> a monta.
/// </summary>
/// <param name="SchemaVersion">Versão do formato (RNF09).</param>
/// <param name="Id">Identificador do cenário (ex.: <c>caldeira</c>).</param>
/// <param name="Description">Nota para quem edita o arquivo; não é texto de interface.</param>
/// <param name="StepSeconds">Passo fixo de integração, em segundos (D06).</param>
/// <param name="NoiseSeed">Semente do ruído dos instrumentos (D06, D22).</param>
/// <param name="Components">Componentes, na ordem do arquivo.</param>
/// <param name="Connections">Ligações saída → entrada entre componentes.</param>
/// <param name="Inputs">Entradas externas da planta (comandos e cargas).</param>
/// <param name="Outputs">Saídas externas da planta (medições e estados).</param>
/// <param name="Io">Mapa Modbus (seção <c>io</c>); nulo se o cenário não tiver.</param>
public sealed record ScenarioDefinition(
    int SchemaVersion,
    string Id,
    string? Description,
    double StepSeconds,
    ulong NoiseSeed,
    IReadOnlyList<ComponentDefinition> Components,
    IReadOnlyList<ConnectionDefinition> Connections,
    IReadOnlyList<InputDefinition> Inputs,
    IReadOnlyList<OutputDefinition> Outputs,
    IoDefinition? Io = null);

/// <summary>Um componente: identificador (a tag), tipo, parâmetros e condições iniciais.</summary>
public sealed record ComponentDefinition(
    string Id,
    string Type,
    IReadOnlyDictionary<string, ScenarioValue> Parameters,
    IReadOnlyDictionary<string, ScenarioValue> Initial);

/// <summary>Ligação da saída <c>componente.sinal</c> à entrada <c>componente.sinal</c>.</summary>
public sealed record ConnectionDefinition(string From, string To);

/// <summary>Entrada externa: um nome, as entradas de componentes que ela alimenta e o valor inicial.</summary>
public sealed record InputDefinition(string Name, IReadOnlyList<string> To, double Initial);

/// <summary>Saída externa: um nome para a saída <c>componente.sinal</c>.</summary>
public sealed record OutputDefinition(string Name, string From);

public enum ScenarioValueKind
{
    Number,
    Text,
    Boolean,
}

/// <summary>Valor de um parâmetro como veio do JSON: número, texto ou booleano.</summary>
public readonly record struct ScenarioValue(ScenarioValueKind Kind, double Number, string? Text, bool Boolean)
{
    public static ScenarioValue FromNumber(double value) => new(ScenarioValueKind.Number, value, null, false);

    public static ScenarioValue FromText(string value) => new(ScenarioValueKind.Text, 0.0, value, false);

    public static ScenarioValue FromBoolean(bool value) => new(ScenarioValueKind.Boolean, 0.0, null, value);

    /// <summary>Descrição para mensagens de erro (ex.: <c>o texto "abc"</c>).</summary>
    public string Describe() => Kind switch
    {
        ScenarioValueKind.Number => "o número " + Number.ToString("R", CultureInfo.InvariantCulture),
        ScenarioValueKind.Text => $"o texto \"{Text}\"",
        _ => Boolean ? "o booleano true" : "o booleano false",
    };
}
