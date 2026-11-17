using System.Net;

namespace IndustrialSim.Modbus;

/// <summary>
/// Decide se uma conexão Modbus pode ser atendida (pareamento por IP, D07). Enquanto a decisão não sai, o
/// servidor segura a conexão sem responder às requisições.
/// </summary>
public interface IConnectionGate
{
    /// <summary>
    /// Espera a decisão sobre uma conexão vinda de <paramref name="remoteAddress"/>. Devolve um objeto que fica
    /// com a conexão enquanto ela durar (descartado quando ela fecha) ou nulo para recusar e fechar. Se
    /// <paramref name="cancellationToken"/> for cancelado (o mestre desistiu ou o servidor parou), pode devolver
    /// nulo ou lançar <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<IDisposable?> AuthorizeAsync(IPAddress remoteAddress, CancellationToken cancellationToken);
}
