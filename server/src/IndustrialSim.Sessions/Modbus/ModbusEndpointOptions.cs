using System.Net;

namespace IndustrialSim.Sessions.Modbus;

/// <summary>
/// Servidor Modbus das plantas (seção <c>Simulation:Modbus</c> do appsettings): faixa de portas, endereço de
/// escuta, Unit ID inicial e tempos do pareamento (D07).
/// </summary>
public sealed record ModbusEndpointOptions
{
    /// <summary>
    /// Endereço em que as portas das plantas escutam. O padrão do código é só o loopback (nada exposto sem
    /// configuração); o appsettings do servidor usa <c>0.0.0.0</c>.
    /// </summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>Primeira porta da faixa das plantas (padrão 15000, D07).</summary>
    public int PortRangeStart { get; init; } = 15000;

    /// <summary>Última porta da faixa das plantas (padrão 15999, D07).</summary>
    public int PortRangeEnd { get; init; } = 15999;

    /// <summary>Unit ID com que a planta nasce (padrão 1).</summary>
    public byte DefaultUnitId { get; init; } = 1;

    /// <summary>Tempo para aceitar ou recusar um IP novo; sem resposta, a conexão é fechada (60 s, D07).</summary>
    public TimeSpan PairingTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Depois de uma recusa, as conexões do mesmo IP são fechadas na hora por este tempo, sem novo pedido: um
    /// mestre que reconecta sozinho não enche a IHM de pedidos (D38).
    /// </summary>
    public TimeSpan RefusalCooldown { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Pedidos de pareamento pendentes por planta (IPs diferentes); acima disso, a conexão é fechada.</summary>
    public int MaxPendingRequests { get; init; } = 8;

    /// <summary>Conexões por planta, pendentes ou autorizadas.</summary>
    public int MaxConnections { get; init; } = 8;

    /// <summary>Intervalo entre verificações dos pedidos de pareamento vencidos.</summary>
    public TimeSpan PairingCheckInterval { get; init; } = TimeSpan.FromSeconds(1);

    internal IPAddress ParsedBindAddress => IPAddress.Parse(BindAddress);

    internal void Validate()
    {
        if (!IPAddress.TryParse(BindAddress, out _))
        {
            throw new ArgumentException($"Endereço de escuta do Modbus inválido: '{BindAddress}'.", nameof(BindAddress));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(PortRangeStart, 1, nameof(PortRangeStart));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(PortRangeEnd, IPEndPoint.MaxPort, nameof(PortRangeEnd));
        ArgumentOutOfRangeException.ThrowIfLessThan(PortRangeEnd, PortRangeStart, nameof(PortRangeEnd));
        ArgumentOutOfRangeException.ThrowIfZero(DefaultUnitId, nameof(DefaultUnitId));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(PairingTimeout, TimeSpan.Zero, nameof(PairingTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThan(RefusalCooldown, TimeSpan.Zero, nameof(RefusalCooldown));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPendingRequests, 1, nameof(MaxPendingRequests));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConnections, 1, nameof(MaxConnections));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(PairingCheckInterval, TimeSpan.Zero, nameof(PairingCheckInterval));
    }
}
