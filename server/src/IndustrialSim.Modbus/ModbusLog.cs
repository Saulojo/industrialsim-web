using System.Net;
using Microsoft.Extensions.Logging;

namespace IndustrialSim.Modbus;

/// <summary>
/// Mensagens de log estruturadas do servidor Modbus (geradas pelo <c>LoggerMessage</c>). São mensagens para quem
/// opera o servidor, não texto de interface.
/// </summary>
internal static partial class ModbusLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Servidor Modbus escutando em {EndPoint}.")]
    public static partial void ServerStarted(ILogger logger, EndPoint endPoint);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Servidor Modbus de {EndPoint} encerrado.")]
    public static partial void ServerStopped(ILogger logger, EndPoint endPoint);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error,
        Message = "Erro ao aceitar conexões Modbus em {EndPoint}.")]
    public static partial void AcceptFailed(ILogger logger, Exception exception, EndPoint endPoint);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information,
        Message = "Conexão Modbus {ConnectionId} aberta por {Remote} na porta {Port}.")]
    public static partial void ConnectionOpened(ILogger logger, long connectionId, EndPoint remote, int port);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Information,
        Message = "Conexão Modbus {ConnectionId} de {Remote} autorizada; {DiscardedRequests} requisições recebidas antes da autorização foram descartadas.")]
    public static partial void ConnectionAuthorized(ILogger logger, long connectionId, EndPoint remote, int discardedRequests);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Warning,
        Message = "Conexão Modbus {ConnectionId} de {Remote} recusada pelo pareamento.")]
    public static partial void ConnectionRefused(ILogger logger, long connectionId, EndPoint remote);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Information,
        Message = "Conexão Modbus {ConnectionId} de {Remote} fechada depois de {Requests} requisições.")]
    public static partial void ConnectionClosed(ILogger logger, long connectionId, EndPoint remote, long requests);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Warning,
        Message = "Conexão Modbus de {Remote} recusada na porta {Port}: limite de {MaxConnections} conexões atingido.")]
    public static partial void ConnectionLimitReached(ILogger logger, EndPoint remote, int port, int maxConnections);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Warning,
        Message = "Conexão Modbus {ConnectionId} de {Remote} fechada: quadro inválido (protocolo {ProtocolId}, comprimento {Length}).")]
    public static partial void InvalidFrame(ILogger logger, long connectionId, EndPoint remote, ushort protocolId, ushort length);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Debug,
        Message = "Conexão Modbus {ConnectionId}: exceção {ExceptionCode} na função {Function}.")]
    public static partial void ExceptionResponse(ILogger logger, long connectionId, byte exceptionCode, byte function);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Information,
        Message = "{Count} conexões Modbus de {Address} fechadas por retirada da autorização.")]
    public static partial void ConnectionsClosedByAddress(ILogger logger, int count, IPAddress address);
}
