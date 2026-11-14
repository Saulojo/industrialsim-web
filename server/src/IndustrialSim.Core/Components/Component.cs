using IndustrialSim.Core.Simulation;

namespace IndustrialSim.Core.Components;

/// <summary>
/// Base de todo componente de planta: estado, entradas e saídas como sinais nomeados, e um passo de tempo.
/// </summary>
/// <remarks>
/// Contrato do passo (D06): as entradas são lidas uma vez, no início do passo, e valem constantes até o fim
/// (como a imagem de saídas de um CLP durante o ciclo de varredura). Ao fim do passo, as saídas refletem o estado novo.
/// Um componente não lê relógio, não faz I/O e não tem controlador dentro: só reage às entradas.
/// </remarks>
public abstract class Component
{
    private readonly SignalSet _outputs;
    private readonly SignalSet _states;

    protected Component(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Inputs = new SignalSet(name, "entrada");
        _outputs = new SignalSet(name, "saída");
        _states = new SignalSet(name, "estado");
    }

    /// <summary>Nome da instância (ex.: a tag do equipamento).</summary>
    public string Name { get; }

    /// <summary>Entradas: escritas por quem liga o componente (outro componente, a imagem de I/O, um teste).</summary>
    public SignalSet Inputs { get; }

    /// <summary>Saídas: calculadas pelo componente a partir do estado e das entradas.</summary>
    public IReadOnlySignals Outputs => _outputs;

    /// <summary>Estado: tudo o que o componente precisa guardar entre passos. É o que entra no hash de determinismo.</summary>
    public IReadOnlySignals States => _states;

    protected SignalSet OutputSignals => _outputs;

    protected SignalSet StateSignals => _states;

    /// <summary>Avança o componente de <paramref name="timeSeconds"/> para <paramref name="timeSeconds"/> + <paramref name="stepSeconds"/>.</summary>
    public void Step(double timeSeconds, double stepSeconds)
    {
        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepSeconds), stepSeconds, "O passo deve ser finito e maior que zero.");
        }

        Advance(timeSeconds, stepSeconds);
        ComputeOutputs();
    }

    /// <summary>
    /// Recalcula as saídas a partir do estado e das entradas atuais, sem avançar o tempo.
    /// Útil depois de mudar uma entrada, para ler a saída no início do passo.
    /// </summary>
    public void UpdateOutputs() => ComputeOutputs();

    /// <summary>Substitui o estado (condição inicial ou retomada) e recalcula as saídas.</summary>
    public void SetState(ReadOnlySpan<double> values)
    {
        _states.CopyFrom(values);
        ComputeOutputs();
    }

    /// <summary>Acrescenta o estado deste componente a um hash de determinismo.</summary>
    public void AddStateTo(ref StateHasher hasher) => hasher.Add(_states.Values);

    /// <summary>Integra o estado em um passo, com as entradas lidas no início e mantidas constantes.</summary>
    protected abstract void Advance(double timeSeconds, double stepSeconds);

    /// <summary>Escreve as saídas a partir do estado e das entradas atuais.</summary>
    protected abstract void ComputeOutputs();
}
