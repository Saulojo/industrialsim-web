namespace IndustrialSim.Contracts;

/// <summary>Pedido de pareamento de um IP ainda não autorizado.</summary>
/// <param name="Id">Identificador do pedido, usado para aceitar ou recusar.</param>
/// <param name="Address">IP do mestre.</param>
/// <param name="RequestedUtc">Primeira conexão do IP.</param>
/// <param name="ExpiresUtc">Sem resposta até aqui, as conexões são fechadas.</param>
/// <param name="WaitingConnections">Conexões do IP seguradas pelo pedido.</param>
public sealed record PairingRequestResponse(Guid Id, string Address, DateTimeOffset RequestedUtc, DateTimeOffset ExpiresUtc, int WaitingConnections);

/// <summary>Uma conexão de mestre Modbus.</summary>
/// <param name="Id">Número da conexão.</param>
/// <param name="Address">IP do mestre.</param>
/// <param name="Port">Porta de origem do mestre.</param>
/// <param name="ConnectedUtc">Abertura da conexão.</param>
/// <param name="Authorized">Se já passou pelo pareamento (senão, está pendente).</param>
/// <param name="Requests">Requisições atendidas.</param>
public sealed record ModbusConnectionResponse(long Id, string Address, int Port, DateTimeOffset ConnectedUtc, bool Authorized, long Requests);

/// <summary>Modbus de uma planta (<c>GET /api/plants/{id}/modbus</c>).</summary>
/// <param name="Port">Porta Modbus TCP da planta.</param>
/// <param name="UnitId">Unit ID a que a planta responde.</param>
/// <param name="AuthorizedMasters">IPs autorizados.</param>
/// <param name="PendingRequests">Pedidos de pareamento pendentes.</param>
/// <param name="Connections">Conexões abertas.</param>
/// <param name="Requests">Requisições atendidas desde a criação da planta.</param>
/// <param name="ExceptionResponses">Respostas de exceção enviadas.</param>
public sealed record ModbusInfoResponse(
    int Port,
    int UnitId,
    IReadOnlyList<string> AuthorizedMasters,
    IReadOnlyList<PairingRequestResponse> PendingRequests,
    IReadOnlyList<ModbusConnectionResponse> Connections,
    long Requests,
    long ExceptionResponses);

/// <summary>Áreas Modbus escritas pelo mestre, em <see cref="ModbusWriteResponse"/>.</summary>
public static class ModbusAreas
{
    public const string Coil = "coil";
    public const string HoldingRegister = "holding-register";
}

/// <summary>Última escrita num endereço (<c>GET /api/plants/{id}/modbus/writes</c>).</summary>
/// <param name="Area">Área (<see cref="ModbusAreas"/>).</param>
/// <param name="Address">Endereço em base 0.</param>
/// <param name="ClassicAddress">O mesmo endereço na notação clássica (1, 40001…).</param>
/// <param name="Author">Quem escreveu: o IP do mestre.</param>
/// <param name="TimeUtc">Horário da escrita.</param>
public sealed record ModbusWriteResponse(string Area, int Address, int ClassicAddress, string Author, DateTimeOffset TimeUtc);

/// <summary>Corpo de <c>POST /api/plants/{id}/modbus/masters</c>: autorizar um IP antes de ele conectar.</summary>
/// <param name="Address">IP do mestre.</param>
public sealed record MasterAuthorizeRequest(string Address);

/// <summary>Corpo de <c>PUT /api/plants/{id}/modbus/unit-id</c>.</summary>
/// <param name="UnitId">Unit ID, de 1 a 255.</param>
public sealed record UnitIdRequest(int UnitId);
