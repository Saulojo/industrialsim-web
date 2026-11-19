using System.Diagnostics.CodeAnalysis;
using IndustrialSim.Sessions.Modbus;
using IndustrialSim.Sessions.RealTime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialSim.Sessions;

/// <summary>Limites e tempos do gerenciador de plantas (seção <c>Simulation:Instances</c> do appsettings).</summary>
public sealed record InstanceManagerOptions
{
    /// <summary>Plantas em execução ao mesmo tempo, no máximo (RF06 pede pelo menos 30).</summary>
    public int MaxInstances { get; init; } = 40;

    /// <summary>Tempo sem cliente e sem atividade até a planta expirar (D14: 30 min).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Intervalo entre verificações de expiração.</summary>
    public TimeSpan ExpiryCheckInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Pasta das gravações de ensaio; um arquivo <c>{id da planta}.csv</c> por planta gravada.</summary>
    public string RecordingDirectory { get; init; } = "recordings";

    /// <summary>Uma linha de medição na gravação a cada tantos passos (padrão: 10, ou seja, 100 ms).</summary>
    public int RecordingSampleEverySteps { get; init; } = 10;

    /// <summary>
    /// Estado publicado para a IHM a cada tantos passos (padrão: 5, ou seja, 20 Hz com o passo de 10 ms; RNF04
    /// pede de 20 a 30 Hz).
    /// </summary>
    public int StatePublishEverySteps { get; init; } = 5;

    /// <summary>Servidor Modbus das plantas (seção <c>Simulation:Modbus</c>).</summary>
    public ModbusEndpointOptions Modbus { get; init; } = new();

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Modbus, nameof(Modbus));
        Modbus.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxInstances, 1, nameof(MaxInstances));
        ArgumentException.ThrowIfNullOrWhiteSpace(RecordingDirectory, nameof(RecordingDirectory));
        ArgumentOutOfRangeException.ThrowIfLessThan(RecordingSampleEverySteps, 1, nameof(RecordingSampleEverySteps));
        ArgumentOutOfRangeException.ThrowIfLessThan(StatePublishEverySteps, 1, nameof(StatePublishEverySteps));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(IdleTimeout, TimeSpan.Zero, nameof(IdleTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ExpiryCheckInterval, TimeSpan.Zero, nameof(ExpiryCheckInterval));
    }
}

/// <summary>Resultado de <see cref="InstanceManager.Create"/>.</summary>
public enum CreatePlantStatus
{
    Created,
    ScenarioNotFound,
    LimitReached,

    /// <summary>Nenhuma porta Modbus livre na faixa configurada.</summary>
    ModbusPortsExhausted,
}

/// <summary>Pedido de criação de uma planta.</summary>
/// <param name="ScenarioId">Cenário do catálogo.</param>
/// <param name="NoiseSeed">Semente do ruído; nula para usar a do cenário.</param>
/// <param name="OwnerId">Dono; nulo para visitante (contas chegam na Etapa 10).</param>
/// <param name="Record">Gravar o ensaio em CSV desde o primeiro passo (RF07).</param>
public sealed record CreatePlantRequest(string ScenarioId, ulong? NoiseSeed = null, string? OwnerId = null, bool Record = false);

/// <summary>Resultado de <see cref="InstanceManager.Create"/>: a planta, se criada.</summary>
public sealed record CreatePlantResult(CreatePlantStatus Status, PlantInstance? Instance);

/// <summary>
/// Plantas em execução no servidor: cria, lista, encerra e expira por falta de cliente, com limite de plantas
/// ao mesmo tempo. Cada planta é isolada: tem a sua planta, a sua imagem de I/O, o seu gerador de ruído e a sua
/// porta Modbus, com o seu pareamento.
/// </summary>
public sealed class InstanceManager : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, PlantInstance> _instances = [];

    // Sinalizado quando o primeiro Dispose encerra todas as plantas; não é descartado, para quem espera nele.
    private readonly ManualResetEventSlim _allEnded = new(initialState: false);
    private readonly RealTimeLoop _loop;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly PortAllocator _ports;
    private ITimer? _expiryTimer;
    private ITimer? _pairingTimer;
    private bool _disposed;

    public InstanceManager(
        ScenarioCatalog catalog,
        RealTimeLoop loop,
        InstanceManagerOptions? options = null,
        TimeProvider? time = null,
        ILogger<InstanceManager>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(loop);
        Catalog = catalog;
        _loop = loop;
        Options = options ?? new InstanceManagerOptions();
        Options.Validate();
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<InstanceManager>.Instance;
        _ports = new PortAllocator(Options.Modbus.PortRangeStart, Options.Modbus.PortRangeEnd);
    }

    public ScenarioCatalog Catalog { get; }

    public InstanceManagerOptions Options { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _instances.Count;
            }
        }
    }

    /// <summary>Portas Modbus em uso pelas plantas em execução.</summary>
    public int ModbusPortsInUse => _ports.InUseCount;

    /// <summary>
    /// Passa a verificar a expiração das plantas a cada <see cref="InstanceManagerOptions.ExpiryCheckInterval"/> e
    /// os pedidos de pareamento vencidos a cada <see cref="ModbusEndpointOptions.PairingCheckInterval"/>.
    /// </summary>
    public void StartExpiryTimer()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _expiryTimer ??= _time.CreateTimer(_ => ExpireIdle(), null, Options.ExpiryCheckInterval, Options.ExpiryCheckInterval);
            TimeSpan pairing = Options.Modbus.PairingCheckInterval;
            _pairingTimer ??= _time.CreateTimer(_ => ExpirePairingRequests(), null, pairing, pairing);
        }
    }

    /// <summary>Cria uma planta e a põe no laço; ela dá o primeiro passo no próximo ciclo.</summary>
    public CreatePlantResult Create(CreatePlantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Catalog.TryGet(request.ScenarioId, out ScenarioEntry? scenario))
        {
            return new CreatePlantResult(CreatePlantStatus.ScenarioNotFound, null);
        }

        PlantInstance instance;
        int count;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_instances.Count >= Options.MaxInstances)
            {
                SessionsLog.InstanceLimitReached(_logger, scenario.Id, Options.MaxInstances);
                return new CreatePlantResult(CreatePlantStatus.LimitReached, null);
            }

            ulong seed = request.NoiseSeed ?? scenario.Definition.NoiseSeed;
            var id = Guid.NewGuid();
            (string, int)? recording = request.Record
                ? (Path.Combine(Options.RecordingDirectory, $"{id:N}.csv"), Options.RecordingSampleEverySteps)
                : null;
            try
            {
                instance = new PlantInstance(id, scenario, seed, request.OwnerId, recording, Options.StatePublishEverySteps, StartModbus, _time, _logger);
            }
            catch (ModbusPortsExhaustedException)
            {
                SessionsLog.ModbusPortsExhausted(_logger, scenario.Id, Options.Modbus.PortRangeStart, Options.Modbus.PortRangeEnd);
                return new CreatePlantResult(CreatePlantStatus.ModbusPortsExhausted, null);
            }

            instance.AttachToLoop(_loop.Add(instance));
            _instances.Add(instance.Id, instance);
            count = _instances.Count;
        }

        SessionsLog.InstanceCreated(_logger, instance.Id, scenario.Id, instance.NoiseSeed, instance.LoopThreadIndex, count);
        return new CreatePlantResult(CreatePlantStatus.Created, instance);
    }

    public bool TryGet(Guid id, [NotNullWhen(true)] out PlantInstance? instance)
    {
        lock (_gate)
        {
            return _instances.TryGetValue(id, out instance);
        }
    }

    /// <summary>Plantas em execução, da mais antiga para a mais nova.</summary>
    public IReadOnlyList<PlantInstance> List()
    {
        lock (_gate)
        {
            return [.. _instances.Values.OrderBy(i => i.CreatedUtc).ThenBy(i => i.Id)];
        }
    }

    /// <summary>Encerra uma planta: sai do laço e do gerenciador. Falso se não existir.</summary>
    public bool End(Guid id, EndReason reason = EndReason.Requested)
    {
        PlantInstance? instance;
        int count;
        lock (_gate)
        {
            if (!_instances.Remove(id, out instance))
            {
                return false;
            }

            count = _instances.Count;
        }

        bool ended = instance.End(reason);
        _ports.Release(instance.Modbus.Port);
        if (ended)
        {
            SessionsLog.InstanceEnded(_logger, instance.Id, reason, Math.Round(instance.Tracking.SimulatedSeconds, 3), count);
        }

        return true;
    }

    /// <summary>
    /// Encerra as plantas sem cliente cuja última atividade foi há <see cref="InstanceManagerOptions.IdleTimeout"/>
    /// ou mais. Devolve quantas expiraram.
    /// </summary>
    public int ExpireIdle()
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<Guid> idle;
        lock (_gate)
        {
            idle = [.. _instances.Values
                .Where(i => i.ClientCount == 0 && now - i.LastActivityUtc >= Options.IdleTimeout)
                .Select(i => i.Id)];
        }

        int expired = 0;
        foreach (Guid id in idle)
        {
            if (End(id, EndReason.Expired))
            {
                expired++;
            }
        }

        return expired;
    }

    /// <summary>
    /// Fecha, em todas as plantas, os pedidos de pareamento sem resposta no tempo limite (D07: 60 s). Devolve
    /// quantos venceram.
    /// </summary>
    public int ExpirePairingRequests() => List().Sum(i => i.Modbus.Pairing.ExpirePending());

    /// <summary>
    /// Para a verificação de expiração e encerra todas as plantas. Uma chamada simultânea espera a primeira
    /// terminar.
    /// </summary>
    public void Dispose()
    {
        List<Guid> ids;
        bool alreadyDisposing;
        lock (_gate)
        {
            alreadyDisposing = _disposed;
            _disposed = true;
            _expiryTimer?.Dispose();
            _expiryTimer = null;
            _pairingTimer?.Dispose();
            _pairingTimer = null;
            ids = alreadyDisposing ? [] : [.. _instances.Keys];
        }

        if (alreadyDisposing)
        {
            _allEnded.Wait();
            return;
        }

        foreach (Guid id in ids)
        {
            End(id, EndReason.Shutdown);
        }

        _allEnded.Set();
    }

    private PlantModbus? StartModbus(PlantInstance instance) =>
        PlantModbus.Start(instance.Id, instance.Io.Image, instance.AttachClient, Options.Modbus, _ports, _time, _logger);
}
