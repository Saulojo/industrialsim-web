namespace IndustrialSim.Sessions;

/// <summary>Tipos de evento do log de comunicação (<see cref="PlantEventLog"/>); a IHM traduz cada um.</summary>
public static class PlantEventKinds
{
    /// <summary>Conexão Modbus autorizada e atendida. Argumentos: <c>address</c>, <c>port</c>, <c>connection</c>.</summary>
    public const string ConnectionAuthorized = "connection-authorized";

    /// <summary>Conexão fechada pelo pareamento. Argumentos: <c>address</c>.</summary>
    public const string ConnectionRefused = "connection-refused";

    /// <summary>Conexão autorizada fechada. Argumentos: <c>address</c>, <c>port</c>, <c>connection</c>, <c>requests</c>.</summary>
    public const string ConnectionClosed = "connection-closed";

    /// <summary>Conexão recusada pelo limite de conexões da planta. Argumentos: <c>address</c>.</summary>
    public const string ConnectionLimit = "connection-limit";

    /// <summary>Pedido de pareamento aberto. Argumentos: <c>address</c>.</summary>
    public const string PairingRequested = "pairing-requested";

    /// <summary>Pedido aceito. Argumentos: <c>address</c>, <c>author</c>.</summary>
    public const string PairingAccepted = "pairing-accepted";

    /// <summary>Pedido recusado. Argumentos: <c>address</c>, <c>author</c>.</summary>
    public const string PairingRefused = "pairing-refused";

    /// <summary>Pedido sem resposta no prazo. Argumentos: <c>address</c>.</summary>
    public const string PairingExpired = "pairing-expired";

    /// <summary>IP autorizado antes de conectar. Argumentos: <c>address</c>, <c>author</c>.</summary>
    public const string MasterAuthorized = "master-authorized";

    /// <summary>Autorização retirada. Argumentos: <c>address</c>, <c>author</c>, <c>connections</c>.</summary>
    public const string MasterRevoked = "master-revoked";

    /// <summary>
    /// Autorização retirada porque outro mestre foi aceito no modo "um mestre por vez" (D42). Argumentos:
    /// <c>address</c>, <c>replacement</c>, <c>connections</c>.
    /// </summary>
    public const string MasterReplaced = "master-replaced";

    /// <summary>
    /// Resposta de exceção Modbus. Argumentos: <c>address</c>, <c>function</c>, <c>code</c> e, se a função tiver,
    /// <c>register</c>. Exceções iguais seguidas são agrupadas (<see cref="PlantEvent.Count"/>).
    /// </summary>
    public const string ModbusException = "modbus-exception";

    /// <summary>Configuração alterada. Argumentos: <c>setting</c>, <c>old</c>, <c>new</c>, <c>author</c>.</summary>
    public const string SettingChanged = "setting-changed";

    /// <summary>Entrada forçada. Argumentos: <c>area</c>, <c>signal</c>, <c>value</c>, <c>author</c>.</summary>
    public const string InputForced = "input-forced";

    /// <summary>Entrada liberada. Argumentos: <c>area</c>, <c>signal</c>, <c>author</c>.</summary>
    public const string InputReleased = "input-released";

    /// <summary>Planta pausada. Argumentos: <c>author</c>, <c>step</c>.</summary>
    public const string Paused = "paused";

    /// <summary>Planta retomada. Argumentos: <c>author</c>, <c>step</c>.</summary>
    public const string Resumed = "resumed";

    /// <summary>Planta reiniciada (D41). Argumentos: <c>author</c>, <c>run</c>.</summary>
    public const string Restarted = "restarted";
}

/// <summary>Nomes das configurações em <see cref="PlantEventKinds.SettingChanged"/>.</summary>
public static class PlantSettingNames
{
    public const string Format = "format";
    public const string WordOrder = "wordOrder";
    public const string WatchdogEnabled = "watchdogEnabled";
    public const string WatchdogTimeout = "watchdogTimeoutSeconds";
    public const string DiagnosticsEnabled = "diagnosticsEnabled";
    public const string UnitId = "unitId";
    public const string MasterPolicy = "masterPolicy";
}

/// <summary>Um argumento de um evento do log.</summary>
public readonly record struct PlantEventArgument(string Name, string Value);

/// <summary>
/// Um evento do log de comunicação: tipo, argumentos e horário. Eventos agrupados (<see cref="Count"/> &gt; 1)
/// guardam o horário da primeira e da última ocorrência.
/// </summary>
/// <param name="Id">Identificador do evento; não muda quando ele é agrupado.</param>
/// <param name="Sequence">Ordem do evento no log; muda (cresce) a cada nova ocorrência agrupada.</param>
/// <param name="Kind">Tipo (<see cref="PlantEventKinds"/>).</param>
/// <param name="FirstUtc">Primeira ocorrência.</param>
/// <param name="TimeUtc">Última ocorrência.</param>
/// <param name="Count">Ocorrências agrupadas.</param>
/// <param name="Arguments">Argumentos, na ordem em que foram dados.</param>
public sealed record PlantEvent(
    long Id,
    long Sequence,
    string Kind,
    DateTimeOffset FirstUtc,
    DateTimeOffset TimeUtc,
    int Count,
    IReadOnlyList<PlantEventArgument> Arguments);

/// <summary>
/// Log de comunicação de uma planta (RF05, Etapa 8): conexões, pareamentos, exceções Modbus, mudanças de
/// configuração, valores forçados, pausa e reinício. Guarda os últimos <see cref="Capacity"/> eventos, em memória
/// (a persistência vem com as contas, na Etapa 10). Thread-safe.
/// </summary>
/// <remarks>
/// Os eventos levam só o tipo e os argumentos; o texto vem dos arquivos de idioma da IHM (RNF11). Um evento
/// agrupável (exceções Modbus, recusas e limite de conexões) igual ao último do mesmo tipo e argumentos, ocorrido
/// há menos de <see cref="GroupingWindow"/>, não cria outra linha: a linha existente ganha uma ocorrência e vai
/// para o fim do log. Assim um mestre que pede um endereço inválido a cada ciclo não enche o log.
/// </remarks>
public sealed class PlantEventLog
{
    /// <summary>Eventos guardados, no máximo.</summary>
    public const int Capacity = 500;

    /// <summary>Até quanto tempo depois da última ocorrência um evento igual é agrupado.</summary>
    public static readonly TimeSpan GroupingWindow = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> Groupable = new(StringComparer.Ordinal)
    {
        PlantEventKinds.ModbusException,
        PlantEventKinds.ConnectionRefused,
        PlantEventKinds.ConnectionLimit,
    };

    private readonly Lock _gate = new();
    private readonly LinkedList<PlantEvent> _events = new();
    private readonly TimeProvider _time;
    private long _nextId;
    private long _sequence;

    public PlantEventLog(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Sequência do evento mais recente (0 se o log está vazio); cresce a cada evento ou agrupamento.</summary>
    public long LastSequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    /// <summary>Registra um evento e o devolve (o mesmo, com mais uma ocorrência, se foi agrupado).</summary>
    public PlantEvent Add(string kind, params ReadOnlySpan<PlantEventArgument> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        PlantEventArgument[] args = arguments.ToArray();
        DateTimeOffset now = _time.GetUtcNow();
        lock (_gate)
        {
            long sequence = ++_sequence;
            if (Groupable.Contains(kind) && FindGroup(kind, args, now) is { } node)
            {
                PlantEvent grouped = node.Value with { Sequence = sequence, TimeUtc = now, Count = node.Value.Count + 1 };
                _events.Remove(node);
                node.Value = grouped;
                _events.AddLast(node);
                return grouped;
            }

            var entry = new PlantEvent(++_nextId, sequence, kind, now, now, 1, args);
            _events.AddLast(entry);
            if (_events.Count > Capacity)
            {
                _events.RemoveFirst();
            }

            return entry;
        }
    }

    /// <summary>Eventos com sequência maior que <paramref name="afterSequence"/>, do mais antigo para o mais novo.</summary>
    public IReadOnlyList<PlantEvent> Since(long afterSequence)
    {
        lock (_gate)
        {
            var result = new List<PlantEvent>();
            for (LinkedListNode<PlantEvent>? node = _events.Last; node is not null && node.Value.Sequence > afterSequence; node = node.Previous)
            {
                result.Add(node.Value);
            }

            result.Reverse();
            return result;
        }
    }

    /// <summary>Todos os eventos guardados, do mais antigo para o mais novo.</summary>
    public IReadOnlyList<PlantEvent> All() => Since(0);

    private LinkedListNode<PlantEvent>? FindGroup(string kind, PlantEventArgument[] args, DateTimeOffset now)
    {
        // Procura do fim para o começo, só dentro da janela de agrupamento.
        for (LinkedListNode<PlantEvent>? node = _events.Last; node is not null; node = node.Previous)
        {
            PlantEvent candidate = node.Value;
            if (now - candidate.TimeUtc >= GroupingWindow)
            {
                return null;
            }

            if (candidate.Kind == kind && candidate.Arguments.SequenceEqual(args))
            {
                return node;
            }
        }

        return null;
    }
}
