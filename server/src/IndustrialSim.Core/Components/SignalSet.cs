namespace IndustrialSim.Core.Components;

/// <summary>Conjunto de sinais nomeados, só para leitura.</summary>
public interface IReadOnlySignals
{
    int Count { get; }

    /// <summary>Nomes na ordem em que foram declarados; o índice de cada nome é o índice do valor.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Valores na ordem de declaração.</summary>
    ReadOnlySpan<double> Values { get; }

    double this[int index] { get; }

    double this[string name] { get; }

    bool Contains(string name);

    /// <summary>Índice do sinal; lança <see cref="KeyNotFoundException"/> com os nomes válidos se não existir.</summary>
    int IndexOf(string name);
}

/// <summary>
/// Sinais nomeados de um componente (entradas, saídas ou estados). O acesso por nome serve à montagem
/// (ligações de cenário, Etapa 3); dentro do passo, os componentes usam o índice, sem busca.
/// </summary>
public sealed class SignalSet : IReadOnlySignals
{
    private readonly string _owner;
    private readonly string _kind;
    private readonly List<string> _names = [];
    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);
    private double[] _values = [];

    /// <param name="owner">Nome do componente dono, para as mensagens de erro.</param>
    /// <param name="kind">Tipo dos sinais ("entrada", "saída", "estado"), para as mensagens de erro.</param>
    public SignalSet(string owner, string kind)
    {
        _owner = owner;
        _kind = kind;
    }

    public int Count => _names.Count;

    public IReadOnlyList<string> Names => _names;

    public ReadOnlySpan<double> Values => _values;

    public double this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }

    public double this[string name]
    {
        get => _values[IndexOf(name)];
        set => _values[IndexOf(name)] = value;
    }

    /// <summary>Declara um sinal e devolve o índice dele.</summary>
    public int Add(string name, double initialValue = 0.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_indexes.ContainsKey(name))
        {
            throw new ArgumentException($"O componente '{_owner}' já tem um sinal de {_kind} chamado '{name}'.", nameof(name));
        }

        int index = _names.Count;
        _names.Add(name);
        _indexes.Add(name, index);
        Array.Resize(ref _values, index + 1);
        _values[index] = initialValue;
        return index;
    }

    public bool Contains(string name) => _indexes.ContainsKey(name);

    public int IndexOf(string name)
    {
        if (_indexes.TryGetValue(name, out int index))
        {
            return index;
        }

        throw new KeyNotFoundException(
            $"O componente '{_owner}' não tem sinal de {_kind} chamado '{name}'. Disponíveis: {string.Join(", ", _names)}.");
    }

    /// <summary>Acesso direto aos valores, para integradores vetoriais dentro do núcleo.</summary>
    internal Span<double> AsSpan() => _values;

    /// <summary>Copia todos os valores de uma vez (mesma ordem de declaração).</summary>
    public void CopyFrom(ReadOnlySpan<double> values)
    {
        if (values.Length != _values.Length)
        {
            throw new ArgumentException(
                $"O componente '{_owner}' tem {_values.Length} sinais de {_kind}; foram dados {values.Length} valores.", nameof(values));
        }

        values.CopyTo(_values);
    }
}
