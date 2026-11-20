using System.Net;
using System.Net.Sockets;
using IndustrialSim.IO;
using IndustrialSim.Modbus;
using Microsoft.Extensions.Logging;

namespace IndustrialSim.Sessions.Modbus;

/// <summary>
/// O Modbus de uma planta em execução: a porta, o escravo ligado à imagem de I/O, o servidor TCP e o
/// pareamento dos mestres (RF03, D07).
/// </summary>
public sealed class PlantModbus : IDisposable
{
    private readonly ModbusTcpServer _server;
    private readonly TimeProvider _time;
    private readonly Lock _rateGate = new();
    private long _sampledRequests;
    private long _sampledTimestamp;
    private double _requestsPerSecond;

    private PlantModbus(ModbusTcpServer server, MasterPairing pairing, int port, TimeProvider time)
    {
        _server = server;
        Pairing = pairing;
        Port = port;
        _time = time;
        _sampledTimestamp = time.GetTimestamp();
    }

    /// <summary>
    /// Requisições por segundo, medidas entre as duas últimas amostras (<see cref="SampleRequestRate"/>, a cada
    /// segundo pelo gerenciador de plantas).
    /// </summary>
    public double RequestsPerSecond
    {
        get
        {
            lock (_rateGate)
            {
                return _requestsPerSecond;
            }
        }
    }

    /// <summary>Porta da planta, na faixa configurada.</summary>
    public int Port { get; }

    /// <summary>Endereço e porta em que a planta escuta.</summary>
    public IPEndPoint LocalEndPoint => _server.LocalEndPoint!;

    /// <summary>Escravo Modbus: Unit ID, contadores e autor de cada escrita.</summary>
    public ModbusDevice Device => _server.Device;

    public MasterPairing Pairing { get; }

    /// <summary>Unit ID a que a planta responde; muda com a planta rodando.</summary>
    public byte UnitId
    {
        get => Device.UnitId;
        set => Device.UnitId = value;
    }

    /// <summary>Conexões abertas (pendentes e autorizadas).</summary>
    public IReadOnlyList<ModbusConnectionInfo> Connections => _server.Connections;

    /// <summary>
    /// Cria o Modbus da planta e o põe para escutar numa porta livre da faixa. Nulo se nenhuma porta servir.
    /// </summary>
    internal static PlantModbus? Start(
        Guid plantId,
        IoImage image,
        Func<IDisposable> attachClient,
        PlantEventLog events,
        ModbusEndpointOptions options,
        PortAllocator ports,
        TimeProvider time,
        ILogger logger)
    {
        var pairing = new MasterPairing(plantId, options, attachClient, events, time, logger);
        var observer = new ModbusEventObserver(events);
        var device = new ModbusDevice(image, options.DefaultUnitId, time);
        var serverOptions = new ModbusServerOptions { MaxConnections = options.MaxConnections };
        IPAddress bindAddress = options.ParsedBindAddress;
        ModbusTcpServer? server = null;

        int? port = ports.Allocate(candidate =>
        {
            var attempt = new ModbusTcpServer(device, pairing, serverOptions, time, logger, observer);
            try
            {
                attempt.Start(new IPEndPoint(bindAddress, candidate));
                server = attempt;
                return true;
            }
            catch (SocketException)
            {
                attempt.Dispose();
                return false;
            }
        });

        if (port is not int allocated || server is null)
        {
            return null;
        }

        pairing.CloseConnections = server.CloseConnections;
        return new PlantModbus(server, pairing, allocated, time);
    }

    /// <summary>Mede a taxa de requisições desde a amostra anterior.</summary>
    public void SampleRequestRate()
    {
        long requests = Device.Requests;
        long now = _time.GetTimestamp();
        lock (_rateGate)
        {
            double seconds = (double)(now - _sampledTimestamp) / _time.TimestampFrequency;
            if (seconds <= 0.0)
            {
                return;
            }

            _requestsPerSecond = (requests - _sampledRequests) / seconds;
            _sampledRequests = requests;
            _sampledTimestamp = now;
        }
    }

    /// <summary>Para de escutar e fecha todas as conexões, inclusive as pendentes.</summary>
    public void Dispose()
    {
        Pairing.CancelPending();
        _server.Dispose();
    }
}
