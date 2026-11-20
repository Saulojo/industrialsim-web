using System.Globalization;
using System.Net;
using IndustrialSim.Modbus;

namespace IndustrialSim.Sessions.Modbus;

/// <summary>Leva os eventos do servidor Modbus da planta para o log de comunicação (<see cref="PlantEventLog"/>).</summary>
internal sealed class ModbusEventObserver(PlantEventLog events) : IModbusServerObserver
{
    public void ConnectionAuthorized(long connectionId, IPEndPoint remote) =>
        events.Add(PlantEventKinds.ConnectionAuthorized, Address(remote), Port(remote), Connection(connectionId));

    public void ConnectionRefused(long connectionId, IPEndPoint remote) =>
        events.Add(PlantEventKinds.ConnectionRefused, Address(remote));

    public void ConnectionClosed(long connectionId, IPEndPoint remote, long requests) =>
        events.Add(
            PlantEventKinds.ConnectionClosed,
            Address(remote),
            Port(remote),
            Connection(connectionId),
            new PlantEventArgument("requests", requests.ToString(CultureInfo.InvariantCulture)));

    public void ConnectionLimitReached(IPEndPoint remote) =>
        events.Add(PlantEventKinds.ConnectionLimit, Address(remote));

    public void ExceptionResponse(long connectionId, IPEndPoint remote, byte function, byte exceptionCode, int? address)
    {
        PlantEventArgument[] arguments =
        [
            Address(remote),
            new("function", function.ToString(CultureInfo.InvariantCulture)),
            new("code", exceptionCode.ToString(CultureInfo.InvariantCulture)),
        ];
        if (address is int register)
        {
            arguments = [.. arguments, new PlantEventArgument("register", register.ToString(CultureInfo.InvariantCulture))];
        }

        events.Add(PlantEventKinds.ModbusException, arguments);
    }

    private static PlantEventArgument Address(IPEndPoint remote) =>
        new("address", ModbusTcpServer.Normalize(remote.Address).ToString());

    private static PlantEventArgument Port(IPEndPoint remote) =>
        new("port", remote.Port.ToString(CultureInfo.InvariantCulture));

    private static PlantEventArgument Connection(long id) =>
        new("connection", id.ToString(CultureInfo.InvariantCulture));
}
