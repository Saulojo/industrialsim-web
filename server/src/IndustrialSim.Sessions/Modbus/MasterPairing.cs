using System.Net;
using IndustrialSim.Modbus;
using Microsoft.Extensions.Logging;

namespace IndustrialSim.Sessions.Modbus;

/// <summary>Pedido de pareamento de um IP ainda não autorizado.</summary>
/// <param name="Id">Identificador do pedido (usado para aceitar ou recusar).</param>
/// <param name="Address">IP do mestre.</param>
/// <param name="RequestedUtc">Primeira conexão do IP.</param>
/// <param name="ExpiresUtc">Sem resposta até aqui, as conexões são fechadas.</param>
/// <param name="WaitingConnections">Conexões do IP seguradas pelo pedido.</param>
public sealed record PairingRequestInfo(Guid Id, IPAddress Address, DateTimeOffset RequestedUtc, DateTimeOffset ExpiresUtc, int WaitingConnections);

/// <summary>
/// Pareamento por IP dos mestres Modbus de uma planta (D07, D38).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Uma conexão de IP autorizado é atendida na hora.</item>
/// <item>A primeira conexão de um IP novo abre um pedido e fica segura, sem resposta. Outras conexões do mesmo
/// IP esperam o mesmo pedido.</item>
/// <item>Aceito, o IP passa a ser autorizado e as conexões seguradas são atendidas.</item>
/// <item>Recusado, as conexões são fechadas e, por <see cref="ModbusEndpointOptions.RefusalCooldown"/>, novas
/// conexões do IP são fechadas na hora, sem novo pedido.</item>
/// <item>Sem resposta em <see cref="ModbusEndpointOptions.PairingTimeout"/>, as conexões são fechadas; a próxima
/// conexão abre outro pedido.</item>
/// <item>Retirar a autorização fecha as conexões do IP; a próxima conexão abre outro pedido.</item>
/// </list>
/// Até as contas (Etapa 10), quem pode aceitar e recusar é quem tem o identificador da planta.
/// </remarks>
public sealed class MasterPairing : IConnectionGate
{
    private readonly Lock _gate = new();
    private readonly HashSet<IPAddress> _authorized = [];
    private readonly Dictionary<IPAddress, PendingRequest> _pending = [];
    private readonly Dictionary<IPAddress, DateTimeOffset> _refusedUntil = [];
    private readonly ModbusEndpointOptions _options;
    private readonly Func<IDisposable> _attachClient;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Guid _plantId;

    /// <param name="plantId">Planta, para os logs.</param>
    /// <param name="attachClient">Registra uma conexão autorizada como cliente da planta (a planta não expira).</param>
    internal MasterPairing(Guid plantId, ModbusEndpointOptions options, Func<IDisposable> attachClient, TimeProvider time, ILogger logger)
    {
        _plantId = plantId;
        _options = options;
        _attachClient = attachClient;
        _time = time;
        _logger = logger;
    }

    /// <summary>Fecha as conexões de um IP; ligado ao servidor da planta.</summary>
    internal Func<IPAddress, int> CloseConnections { get; set; } = static _ => 0;

    /// <summary>IPs autorizados, em ordem.</summary>
    public IReadOnlyList<IPAddress> Authorized
    {
        get
        {
            lock (_gate)
            {
                return [.. _authorized.OrderBy(a => a.ToString(), StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>Pedidos pendentes, do mais antigo para o mais novo.</summary>
    public IReadOnlyList<PairingRequestInfo> Pending
    {
        get
        {
            lock (_gate)
            {
                return [.. _pending.Values.OrderBy(p => p.RequestedUtc).ThenBy(p => p.Id).Select(p => p.ToInfo())];
            }
        }
    }

    public async Task<IDisposable?> AuthorizeAsync(IPAddress remoteAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);
        IPAddress address = ModbusTcpServer.Normalize(remoteAddress);
        PendingRequest request;
        bool created = false;
        lock (_gate)
        {
            if (_authorized.Contains(address))
            {
                return _attachClient();
            }

            DateTimeOffset now = _time.GetUtcNow();
            if (_refusedUntil.TryGetValue(address, out DateTimeOffset until))
            {
                if (now < until)
                {
                    SessionsLog.PairingRecentlyRefused(_logger, _plantId, address);
                    return null;
                }

                _refusedUntil.Remove(address);
            }

            if (!_pending.TryGetValue(address, out PendingRequest? existing))
            {
                if (_pending.Count >= _options.MaxPendingRequests)
                {
                    SessionsLog.PairingPendingLimit(_logger, _plantId, address, _options.MaxPendingRequests);
                    return null;
                }

                existing = new PendingRequest(Guid.NewGuid(), address, now, now + _options.PairingTimeout);
                _pending.Add(address, existing);
                created = true;
            }

            request = existing;
            request.WaitingConnections++;
        }

        if (created)
        {
            SessionsLog.PairingRequested(_logger, _plantId, request.Id, address);
        }

        try
        {
            bool accepted = await request.Decision.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return accepted ? _attachClient() : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            lock (_gate)
            {
                request.WaitingConnections--;
            }
        }
    }

    /// <summary>Aceita um pedido: o IP passa a ser autorizado. Falso se o pedido não existir mais.</summary>
    public bool Accept(Guid requestId)
    {
        PendingRequest? request;
        lock (_gate)
        {
            request = Take(requestId);
            if (request is null)
            {
                return false;
            }

            _authorized.Add(request.Address);
        }

        SessionsLog.PairingAccepted(_logger, _plantId, request.Id, request.Address);
        request.Decision.TrySetResult(true);
        return true;
    }

    /// <summary>Recusa um pedido: as conexões seguradas são fechadas. Falso se o pedido não existir mais.</summary>
    public bool Refuse(Guid requestId)
    {
        PendingRequest? request;
        lock (_gate)
        {
            request = Take(requestId);
            if (request is null)
            {
                return false;
            }

            if (_options.RefusalCooldown > TimeSpan.Zero)
            {
                _refusedUntil[request.Address] = _time.GetUtcNow() + _options.RefusalCooldown;
            }
        }

        SessionsLog.PairingRefused(_logger, _plantId, request.Id, request.Address);
        request.Decision.TrySetResult(false);
        return true;
    }

    /// <summary>
    /// Autoriza um IP antes de ele conectar (ou aceita o pedido pendente dele). Falso se já estava autorizado.
    /// </summary>
    public bool Authorize(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        address = ModbusTcpServer.Normalize(address);
        PendingRequest? pending;
        lock (_gate)
        {
            if (!_authorized.Add(address))
            {
                return false;
            }

            _refusedUntil.Remove(address);
            if (_pending.Remove(address, out pending))
            {
                pending.Decision.TrySetResult(true);
            }
        }

        SessionsLog.MasterAuthorized(_logger, _plantId, address);
        return true;
    }

    /// <summary>Retira a autorização de um IP e fecha as conexões dele. Falso se ele não era autorizado.</summary>
    public bool Revoke(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        address = ModbusTcpServer.Normalize(address);
        lock (_gate)
        {
            if (!_authorized.Remove(address))
            {
                return false;
            }
        }

        int closed = CloseConnections(address);
        SessionsLog.MasterRevoked(_logger, _plantId, address, closed);
        return true;
    }

    /// <summary>Fecha os pedidos sem resposta no tempo limite. Devolve quantos venceram.</summary>
    public int ExpirePending()
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<PendingRequest> expired;
        lock (_gate)
        {
            expired = [.. _pending.Values.Where(p => now >= p.ExpiresUtc)];
            foreach (PendingRequest request in expired)
            {
                _pending.Remove(request.Address);
            }

            // Recusas vencidas também saem, para a lista não crescer.
            foreach (IPAddress address in _refusedUntil.Where(r => now >= r.Value).Select(r => r.Key).ToList())
            {
                _refusedUntil.Remove(address);
            }
        }

        foreach (PendingRequest request in expired)
        {
            SessionsLog.PairingExpired(_logger, _plantId, request.Id, request.Address);
            request.Decision.TrySetResult(false);
        }

        return expired.Count;
    }

    /// <summary>Recusa os pedidos pendentes sem marcar os IPs (a planta está terminando).</summary>
    internal void CancelPending()
    {
        List<PendingRequest> pending;
        lock (_gate)
        {
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (PendingRequest request in pending)
        {
            request.Decision.TrySetResult(false);
        }
    }

    private PendingRequest? Take(Guid requestId)
    {
        PendingRequest? request = _pending.Values.FirstOrDefault(p => p.Id == requestId);
        if (request is not null)
        {
            _pending.Remove(request.Address);
        }

        return request;
    }

    private sealed class PendingRequest(Guid id, IPAddress address, DateTimeOffset requestedUtc, DateTimeOffset expiresUtc)
    {
        public Guid Id { get; } = id;

        public IPAddress Address { get; } = address;

        public DateTimeOffset RequestedUtc { get; } = requestedUtc;

        public DateTimeOffset ExpiresUtc { get; } = expiresUtc;

        /// <summary>Protegido pela trava do pareamento.</summary>
        public int WaitingConnections { get; set; }

        public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PairingRequestInfo ToInfo() => new(Id, Address, RequestedUtc, ExpiresUtc, WaitingConnections);
    }
}
