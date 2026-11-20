using System.Net;

namespace IndustrialSim.Modbus;

/// <summary>
/// Recebe os eventos de comunicação do servidor Modbus de uma planta, para o log de comunicação da IHM (Etapa 8).
/// Os métodos são chamados pelas tarefas das conexões, de várias threads ao mesmo tempo, e devem ser rápidos.
/// </summary>
public interface IModbusServerObserver
{
    /// <summary>Uma conexão passou pelo pareamento e começou a ser atendida.</summary>
    void ConnectionAuthorized(long connectionId, IPEndPoint remote);

    /// <summary>Uma conexão foi fechada pelo pareamento (recusa, prazo vencido ou IP recusado há pouco).</summary>
    void ConnectionRefused(long connectionId, IPEndPoint remote);

    /// <summary>Uma conexão autorizada foi fechada (pelo mestre, pela retirada da autorização ou pelo servidor).</summary>
    void ConnectionClosed(long connectionId, IPEndPoint remote, long requests);

    /// <summary>Uma conexão foi fechada na hora porque a planta já tinha o máximo de conexões.</summary>
    void ConnectionLimitReached(IPEndPoint remote);

    /// <summary>O servidor respondeu com exceção.</summary>
    /// <param name="connectionId">Conexão.</param>
    /// <param name="remote">Mestre.</param>
    /// <param name="function">Código da função pedida (sem o bit de exceção).</param>
    /// <param name="exceptionCode">Código da exceção (01 a 04, 0B…).</param>
    /// <param name="address">Endereço inicial pedido (base 0), se a função tiver um.</param>
    void ExceptionResponse(long connectionId, IPEndPoint remote, byte function, byte exceptionCode, int? address);
}
