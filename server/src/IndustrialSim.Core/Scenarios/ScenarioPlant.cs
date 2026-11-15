using IndustrialSim.Core.Components;
using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Planta montada a partir de um cenário (D04): componentes da biblioteca, ligados pelos sinais nomeados,
/// avançando em passo fixo (D06). Não há código específico por planta, nem controlador: ela só reage às entradas.
/// </summary>
/// <remarks>
/// Ordem de cada passo:
/// 1. as entradas externas (comandos e cargas) são copiadas para as entradas dos componentes; um valor escrito
///    com <see cref="SetInput(int, double)"/> durante um passo só vale a partir do passo seguinte;
/// 2. cada componente, na ordem dada pelas ligações (<see cref="ExecutionOrder"/>), recebe os valores atuais das
///    saídas a que está ligado e avança;
/// 3. o relógio avança.
/// Não é segura para uso por várias threads; a sincronização com a rede é da imagem de I/O (Etapa 4).
/// </remarks>
public sealed class ScenarioPlant
{
    private readonly Component[] _components;
    private readonly Dictionary<string, int> _componentIndexes;
    private readonly int[] _order;
    private readonly Link[][] _links;
    private readonly string[] _inputNames;
    private readonly double[] _inputValues;
    private readonly Target[][] _inputTargets;
    private readonly string[] _outputNames;
    private readonly Source[] _outputSources;

    private ScenarioPlant(
        ScenarioDefinition definition,
        Component[] components,
        int[] order,
        Link[][] links,
        Target[][] inputTargets,
        Source[] outputSources)
    {
        Definition = definition;
        Clock = new SimulationClock(definition.StepSeconds);
        _components = components;
        _componentIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < components.Length; i++)
        {
            _componentIndexes.Add(components[i].Name, i);
        }

        _order = order;
        _links = links;
        _inputNames = [.. definition.Inputs.Select(i => i.Name)];
        _inputValues = [.. definition.Inputs.Select(i => i.Initial)];
        _inputTargets = inputTargets;
        _outputNames = [.. definition.Outputs.Select(o => o.Name)];
        _outputSources = outputSources;

        InitializeComponents();
    }

    public ScenarioDefinition Definition { get; }

    public SimulationClock Clock { get; }

    /// <summary>Componentes, na ordem do arquivo.</summary>
    public IReadOnlyList<Component> Components => _components;

    /// <summary>Identificadores dos componentes na ordem em que avançam dentro do passo.</summary>
    public IReadOnlyList<string> ExecutionOrderIds => [.. _order.Select(i => _components[i].Name)];

    /// <summary>Nomes das entradas externas, na ordem do arquivo.</summary>
    public IReadOnlyList<string> InputNames => _inputNames;

    /// <summary>Nomes das saídas externas, na ordem do arquivo.</summary>
    public IReadOnlyList<string> OutputNames => _outputNames;

    /// <summary>Lê o texto de um <c>scenario.json</c> e monta a planta. Lança <see cref="ScenarioValidationException"/>.</summary>
    public static ScenarioPlant FromJson(string json, ComponentRegistry? registry = null, ulong? noiseSeed = null) =>
        Create(ScenarioLoader.Load(json), registry, noiseSeed);

    /// <summary>
    /// Monta a planta: cria os componentes, confere tipos, parâmetros e ligações e calcula a ordem de execução.
    /// Lança <see cref="ScenarioValidationException"/> com todos os erros, cada um com o caminho do campo.
    /// </summary>
    /// <param name="definition">Cenário lido por <see cref="ScenarioLoader"/>.</param>
    /// <param name="registry">Tipos de componente; por padrão, a biblioteca do projeto.</param>
    /// <param name="noiseSeed">Semente do ruído, no lugar da que está no cenário.</param>
    public static ScenarioPlant Create(ScenarioDefinition definition, ComponentRegistry? registry = null, ulong? noiseSeed = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        registry ??= ComponentRegistry.Default;

        var errors = new List<ScenarioError>();
        int count = definition.Components.Count;
        var components = new Component?[count];
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);

        ulong seed = noiseSeed ?? definition.NoiseSeed;
        ulong seedCounter = 0;
        ulong NextNoiseSeed() => unchecked(seed + seedCounter++);

        for (int i = 0; i < count; i++)
        {
            ComponentDefinition item = definition.Components[i];
            indexes[item.Id] = i;
            components[i] = CreateComponent(item, $"components[{i}]", registry, NextNoiseSeed, errors);
        }

        // Ligações entre componentes.
        var linkLists = new List<Link>[count];
        for (int i = 0; i < count; i++)
        {
            linkLists[i] = [];
        }

        var drivenBy = new Dictionary<(int Component, int Input), string>();
        var edges = new List<(int From, int To)>();
        for (int j = 0; j < definition.Connections.Count; j++)
        {
            ConnectionDefinition connection = definition.Connections[j];
            Port? from = Resolve(connection.From, $"connections[{j}].from", isInput: false, components, indexes, errors);
            Port? to = Resolve(connection.To, $"connections[{j}].to", isInput: true, components, indexes, errors);
            if (to is { } target && !Claim(drivenBy, target, $"connections[{j}].to", connection.To, $"a saída '{connection.From}'", errors))
            {
                continue;
            }

            if (from is { } source && to is { } destination)
            {
                linkLists[destination.Component].Add(new Link(destination.Signal, components[source.Component]!.Outputs, source.Signal));
                edges.Add((source.Component, destination.Component));
            }
        }

        // Entradas externas.
        var inputTargets = new Target[definition.Inputs.Count][];
        for (int k = 0; k < definition.Inputs.Count; k++)
        {
            InputDefinition input = definition.Inputs[k];
            var targets = new List<Target>();
            for (int m = 0; m < input.To.Count; m++)
            {
                string path = $"inputs[{k}].to[{m}]";
                if (Resolve(input.To[m], path, isInput: true, components, indexes, errors) is { } port
                    && Claim(drivenBy, port, path, input.To[m], $"a entrada externa '{input.Name}'", errors))
                {
                    targets.Add(new Target(components[port.Component]!.Inputs, port.Signal));
                }
            }

            inputTargets[k] = [.. targets];
        }

        // Saídas externas.
        var outputSources = new Source[definition.Outputs.Count];
        for (int k = 0; k < definition.Outputs.Count; k++)
        {
            if (Resolve(definition.Outputs[k].From, $"outputs[{k}].from", isInput: false, components, indexes, errors) is { } port)
            {
                outputSources[k] = new Source(components[port.Component]!.Outputs, port.Signal);
            }
        }

        int[] order = [];
        if (errors.Count == 0
            && !ExecutionOrder.TryCompute(count, edges, i => components[i]!.States.Count > 0, out order, out int[] loop))
        {
            string names = string.Join(", ", loop.Select(i => definition.Components[i].Id));
            errors.Add(new ScenarioError(
                "connections",
                $"laço algébrico entre componentes sem estado ({names}): a saída de cada um depende, no mesmo instante, da saída do outro. " +
                "Ponha no laço um componente com dinâmica (ex.: uma válvula ou um transmissor com amortecimento)."));
        }

        if (errors.Count > 0)
        {
            throw new ScenarioValidationException(errors);
        }

        return new ScenarioPlant(
            definition,
            [.. components.Select(c => c!)],
            order,
            [.. linkLists.Select(l => l.ToArray())],
            inputTargets,
            outputSources);
    }

    /// <summary>Índice de uma entrada externa; lança <see cref="KeyNotFoundException"/> com os nomes válidos.</summary>
    public int InputIndex(string name) => IndexOf(_inputNames, name, "entrada externa");

    /// <summary>Índice de uma saída externa; lança <see cref="KeyNotFoundException"/> com os nomes válidos.</summary>
    public int OutputIndex(string name) => IndexOf(_outputNames, name, "saída externa");

    /// <summary>Escreve uma entrada externa. O valor é copiado para a planta no início do próximo passo.</summary>
    public void SetInput(int index, double value) => _inputValues[index] = value;

    public void SetInput(string name, double value) => _inputValues[InputIndex(name)] = value;

    public double GetInput(int index) => _inputValues[index];

    public double GetInput(string name) => _inputValues[InputIndex(name)];

    public double GetOutput(int index)
    {
        Source source = _outputSources[index];
        return source.Signals[source.Index];
    }

    public double GetOutput(string name) => GetOutput(OutputIndex(name));

    /// <summary>Lê a saída <c>componente.sinal</c> de qualquer componente, exposta ou não como saída externa.</summary>
    public double GetSignal(string reference)
    {
        if (!ScenarioLoader.TrySplitReference(reference, out string id, out string signal))
        {
            throw new ArgumentException($"'{reference}' não está no formato 'componente.sinal'.", nameof(reference));
        }

        return FindComponent(id).Outputs[signal];
    }

    /// <summary>Componente pelo identificador; lança <see cref="KeyNotFoundException"/> com os identificadores válidos.</summary>
    public Component FindComponent(string id)
    {
        if (_componentIndexes.TryGetValue(id, out int index))
        {
            return _components[index];
        }

        throw new KeyNotFoundException(
            $"O cenário '{Definition.Id}' não tem componente '{id}'. Disponíveis: {string.Join(", ", _components.Select(c => c.Name))}.");
    }

    /// <summary>Avança a planta em um passo.</summary>
    public void Step()
    {
        double time = Clock.TimeSeconds;
        double step = Clock.StepSeconds;

        ApplyInputs();
        foreach (int index in _order)
        {
            CopyLinks(index);
            _components[index].Step(time, step);
        }

        Clock.Advance();
    }

    /// <summary>
    /// Acrescenta o estado da planta a um hash de determinismo (RNF02): o número do passo e os estados dos
    /// componentes, na ordem do arquivo. O ruído dos instrumentos não é estado físico e fica de fora.
    /// </summary>
    public void AddStateTo(ref StateHasher hasher)
    {
        hasher.Add(Clock.StepCount);
        foreach (Component component in _components)
        {
            component.AddStateTo(ref hasher);
        }
    }

    /// <summary>Hash do estado atual.</summary>
    public ulong StateHash()
    {
        var hasher = default(StateHasher);
        AddStateTo(ref hasher);
        return hasher.Value;
    }

    private static Component? CreateComponent(
        ComponentDefinition item,
        string path,
        ComponentRegistry registry,
        Func<ulong> nextNoiseSeed,
        List<ScenarioError> errors)
    {
        if (!registry.TryGet(item.Type, out ComponentType type))
        {
            errors.Add(new ScenarioError(
                $"{path}.type",
                $"tipo de componente desconhecido: '{item.Type}'. Disponíveis: {string.Join(", ", registry.Types.Select(t => t.Name))}."));
            return null;
        }

        int before = errors.Count;
        ParameterValues parameters = ParameterValues.Validate(type.Parameters, item.Parameters, $"{path}.params", type.Name, errors);
        ParameterValues initial = ParameterValues.Validate(type.Initial, item.Initial, $"{path}.initial", type.Name, errors);
        if (errors.Count > before)
        {
            return null;
        }

        try
        {
            return type.Factory(new ComponentContext(item.Id, parameters, initial, nextNoiseSeed));
        }
        catch (ArgumentException ex)
        {
            // Combinação inválida de parâmetros (ex.: limite inferior acima do superior), recusada pelo próprio componente.
            errors.Add(new ScenarioError($"{path}.params", CleanArgumentMessage(ex.Message)));
            return null;
        }
    }

    /// <summary>Tira o sufixo que o .NET acrescenta às mensagens de argumento ("(Parameter 'x')" e a linha do valor).</summary>
    private static string CleanArgumentMessage(string message)
    {
        int lineEnd = message.IndexOfAny(['\r', '\n']);
        string firstLine = lineEnd >= 0 ? message[..lineEnd] : message;
        int suffix = firstLine.LastIndexOf(" (Parameter '", StringComparison.Ordinal);
        return suffix >= 0 ? firstLine[..suffix] : firstLine;
    }

    /// <summary>Resolve <c>componente.sinal</c>. Nulo se houver erro (já registrado) ou se o componente não pôde ser criado.</summary>
    private static Port? Resolve(
        string reference,
        string path,
        bool isInput,
        Component?[] components,
        Dictionary<string, int> indexes,
        List<ScenarioError> errors)
    {
        ScenarioLoader.TrySplitReference(reference, out string id, out string signal);
        if (!indexes.TryGetValue(id, out int index))
        {
            errors.Add(new ScenarioError(path, $"componente inexistente: '{id}'. Disponíveis: {string.Join(", ", indexes.Keys)}."));
            return null;
        }

        if (components[index] is not { } component)
        {
            return null; // o erro do componente já foi apontado; não repetir em cada ligação
        }

        IReadOnlySignals signals = isInput ? component.Inputs : component.Outputs;
        if (!signals.Contains(signal))
        {
            string kind = isInput ? "entrada" : "saída";
            string available = signals.Count == 0 ? "nenhuma" : string.Join(", ", signals.Names);
            errors.Add(new ScenarioError(path, $"o componente '{id}' não tem {kind} '{signal}'. Disponíveis: {available}."));
            return null;
        }

        return new Port(index, signals.IndexOf(signal));
    }

    /// <summary>Marca a entrada como alimentada; falso (com erro) se ela já tinha outra fonte.</summary>
    private static bool Claim(
        Dictionary<(int Component, int Input), string> drivenBy,
        Port port,
        string path,
        string reference,
        string driver,
        List<ScenarioError> errors)
    {
        if (drivenBy.TryGetValue((port.Component, port.Signal), out string? existing))
        {
            errors.Add(new ScenarioError(path, $"a entrada '{reference}' já recebe {existing}; uma entrada só pode ter uma fonte."));
            return false;
        }

        drivenBy.Add((port.Component, port.Signal), driver);
        return true;
    }

    private static int IndexOf(string[] names, string name, string kind)
    {
        int index = Array.IndexOf(names, name);
        if (index >= 0)
        {
            return index;
        }

        string available = names.Length == 0 ? "nenhuma" : string.Join(", ", names);
        throw new KeyNotFoundException($"O cenário não tem {kind} '{name}'. Disponíveis: {available}.");
    }

    /// <summary>
    /// Deixa as saídas coerentes com as condições iniciais antes do primeiro passo. São duas passagens:
    /// na primeira, cada componente vê as saídas iniciais de quem vem antes dele; na segunda, também as de
    /// quem vem depois (ligações de realimentação).
    /// </summary>
    private void InitializeComponents()
    {
        ApplyInputs();
        foreach (int index in _order)
        {
            CopyLinks(index);
            _components[index].Initialize();
        }

        foreach (int index in _order)
        {
            CopyLinks(index);
            _components[index].UpdateOutputs();
        }
    }

    private void ApplyInputs()
    {
        for (int k = 0; k < _inputTargets.Length; k++)
        {
            double value = _inputValues[k];
            foreach (Target target in _inputTargets[k])
            {
                target.Signals[target.Index] = value;
            }
        }
    }

    private void CopyLinks(int componentIndex)
    {
        SignalSet inputs = _components[componentIndex].Inputs;
        foreach (Link link in _links[componentIndex])
        {
            inputs[link.InputIndex] = link.Source[link.SourceIndex];
        }
    }

    private readonly record struct Port(int Component, int Signal);

    private readonly record struct Link(int InputIndex, IReadOnlySignals Source, int SourceIndex);

    private readonly record struct Target(SignalSet Signals, int Index);

    private readonly record struct Source(IReadOnlySignals Signals, int Index);
}
