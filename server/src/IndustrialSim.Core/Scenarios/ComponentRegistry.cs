using IndustrialSim.Core.Components;

namespace IndustrialSim.Core.Scenarios;

/// <summary>O que a fábrica de um tipo recebe para criar um componente.</summary>
public sealed class ComponentContext
{
    private readonly Func<ulong> _nextNoiseSeed;

    internal ComponentContext(string id, ParameterValues parameters, ParameterValues initial, Func<ulong> nextNoiseSeed)
    {
        Id = id;
        Parameters = parameters;
        Initial = initial;
        _nextNoiseSeed = nextNoiseSeed;
    }

    /// <summary>Identificador do componente no cenário (a tag).</summary>
    public string Id { get; }

    public ParameterValues Parameters { get; }

    public ParameterValues Initial { get; }

    /// <summary>
    /// Semente para um gerador de ruído próprio do componente. A k-ésima chamada na montagem do cenário
    /// (k = 0, 1, 2…) devolve <c>noiseSeed + k</c>; o <see cref="Numerics.SeededRandom"/> espalha sementes vizinhas
    /// com o SplitMix64, de modo que as sequências não se parecem.
    /// </summary>
    public ulong NextNoiseSeed() => _nextNoiseSeed();
}

/// <summary>
/// Um tipo de componente utilizável em cenários: nome no JSON, parâmetros, condições iniciais e fábrica.
/// </summary>
/// <param name="Name">Nome usado no campo <c>type</c> do cenário.</param>
/// <param name="Description">O que o tipo modela (vai para o esquema JSON).</param>
/// <param name="Parameters">Parâmetros aceitos em <c>params</c>.</param>
/// <param name="Initial">Condições iniciais aceitas em <c>initial</c>.</param>
/// <param name="Factory">Cria o componente. Pode lançar <see cref="ArgumentException"/> para combinações inválidas de parâmetros.</param>
public sealed record ComponentType(
    string Name,
    string Description,
    IReadOnlyList<ParameterSpec> Parameters,
    IReadOnlyList<ParameterSpec> Initial,
    Func<ComponentContext, Component> Factory);

/// <summary>
/// Tipos de componente disponíveis para os cenários (D04). Um equipamento novo entra aqui uma vez;
/// uma planta nova é só um JSON que combina os tipos existentes.
/// </summary>
public sealed class ComponentRegistry
{
    private readonly Dictionary<string, ComponentType> _types = new(StringComparer.Ordinal);
    private readonly List<ComponentType> _ordered = [];

    public ComponentRegistry(IEnumerable<ComponentType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        foreach (ComponentType type in types)
        {
            if (!_types.TryAdd(type.Name, type))
            {
                throw new ArgumentException($"O tipo de componente '{type.Name}' foi registrado duas vezes.", nameof(types));
            }

            _ordered.Add(type);
        }
    }

    /// <summary>Biblioteca padrão do projeto (<see cref="StandardComponents"/>).</summary>
    public static ComponentRegistry Default { get; } = new(StandardComponents.All);

    /// <summary>Tipos na ordem de registro.</summary>
    public IReadOnlyList<ComponentType> Types => _ordered;

    public bool TryGet(string name, out ComponentType type) => _types.TryGetValue(name, out type!);
}
