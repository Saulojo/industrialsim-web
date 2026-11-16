using System.Diagnostics.CodeAnalysis;
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

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxInstances, 1, nameof(MaxInstances));
        ArgumentException.ThrowIfNullOrWhiteSpace(RecordingDirectory, nameof(RecordingDirectory));
        ArgumentOutOfRangeException.ThrowIfLessThan(RecordingSampleEverySteps, 1, nameof(RecordingSampleEverySteps));
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
/// ao mesmo tempo. Cada planta é isolada: tem a sua planta, a sua imagem de I/O e o seu gerador de ruído.
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
    private ITimer? _expiryTimer;
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

    /// <summary>Passa a verificar a expiração a cada <see cref="InstanceManagerOptions.ExpiryCheckInterval"/>.</summary>
    public void StartExpiryTimer()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _expiryTimer ??= _time.CreateTimer(_ => ExpireIdle(), null, Options.ExpiryCheckInterval, Options.ExpiryCheckInterval);
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
            instance = new PlantInstance(id, scenario, seed, request.OwnerId, recording, _time, _logger);
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

        if (instance.End(reason))
        {
            SessionsLog.InstanceEnded(_logger, instance.Id, reason, instance.Tracking.SimulatedSeconds, count);
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
}
