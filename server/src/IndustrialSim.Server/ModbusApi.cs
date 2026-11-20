using System.Net;
using IndustrialSim.Contracts;
using IndustrialSim.IO;
using IndustrialSim.Modbus;
using IndustrialSim.Sessions;
using IndustrialSim.Sessions.Modbus;

namespace IndustrialSim.Server;

/// <summary>
/// API REST do Modbus de uma planta (Etapa 6): porta, Unit ID, pareamento dos mestres (D07, D38), conexões e
/// autor da última escrita em cada endereço. A tela de pareamento chega na Etapa 7; até as contas (Etapa 10),
/// quem tem o identificador da planta pode aceitar e recusar mestres.
/// </summary>
internal static class ModbusApi
{
    public static void MapModbusApi(this RouteGroupBuilder plants)
    {
        RouteGroupBuilder modbus = plants.MapGroup("/{id:guid}/modbus");
        modbus.MapGet("/", GetInfo);
        modbus.MapGet("/writes", GetWrites);
        modbus.MapPost("/pairing/{requestId:guid}/accept", Accept);
        modbus.MapPost("/pairing/{requestId:guid}/refuse", Refuse);
        modbus.MapPost("/masters", Authorize);
        modbus.MapDelete("/masters/{address}", Revoke);
        modbus.MapPut("/unit-id", SetUnitId);
    }

    private static IResult GetInfo(Guid id, InstanceManager manager) =>
        WithPlant(id, manager, plant => Results.Ok(ToResponse(plant.Modbus)));

    private static IResult GetWrites(Guid id, InstanceManager manager) =>
        WithPlant(id, manager, plant => Results.Ok(plant.Modbus.Device.Writes.Entries().Select(ToResponse).ToArray()));

    private static IResult Accept(Guid id, Guid requestId, InstanceManager manager, HttpContext context) =>
        WithPlant(id, manager, plant => plant.Modbus.Pairing.Accept(requestId, Author(context)) ? Results.NoContent() : PairingRequestNotFound());

    private static IResult Refuse(Guid id, Guid requestId, InstanceManager manager, HttpContext context) =>
        WithPlant(id, manager, plant => plant.Modbus.Pairing.Refuse(requestId, Author(context)) ? Results.NoContent() : PairingRequestNotFound());

    private static IResult Authorize(Guid id, MasterAuthorizeRequest? request, InstanceManager manager, HttpContext context) =>
        WithPlant(id, manager, plant =>
        {
            if (request is null || !TryParseAddress(request.Address, out IPAddress? address))
            {
                return Results.BadRequest(new ApiError(ApiErrorCodes.InvalidRequest));
            }

            plant.Modbus.Pairing.Authorize(address, Author(context));
            return Results.NoContent();
        });

    private static IResult Revoke(Guid id, string address, InstanceManager manager, HttpContext context) =>
        WithPlant(id, manager, plant =>
        {
            if (!TryParseAddress(address, out IPAddress? parsed))
            {
                return Results.BadRequest(new ApiError(ApiErrorCodes.InvalidRequest));
            }

            return plant.Modbus.Pairing.Revoke(parsed, Author(context))
                ? Results.NoContent()
                : Results.NotFound(new ApiError(ApiErrorCodes.MasterNotFound));
        });

    /// <summary>Muda o Unit ID pela mesma regra da tela de configurações (vai para o log de comunicação).</summary>
    private static IResult SetUnitId(Guid id, UnitIdRequest? request, InstanceManager manager, HttpContext context) =>
        WithPlant(id, manager, plant =>
        {
            if (request is null || request.UnitId is < 1 or > 255)
            {
                return Results.BadRequest(new ApiError(ApiErrorCodes.InvalidRequest));
            }

            return plant.UpdateSettings(new PlantSettingsUpdate(UnitId: (byte)request.UnitId), Author(context)) == SettingsUpdateResult.Applied
                ? Results.NoContent()
                : Results.BadRequest(new ApiError(ApiErrorCodes.InvalidRequest));
        });

    /// <summary>Autor das ações no log: até as contas (Etapa 10), o IP de quem chamou a API.</summary>
    internal static string? Author(HttpContext context) =>
        context.Connection.RemoteIpAddress is IPAddress remote ? ModbusTcpServer.Normalize(remote).ToString() : null;

    /// <summary>Executa a ação na planta, se ela existir, e marca atividade.</summary>
    private static IResult WithPlant(Guid id, InstanceManager manager, Func<PlantInstance, IResult> action)
    {
        if (!manager.TryGet(id, out PlantInstance? plant))
        {
            return SimulationApi.PlantNotFound();
        }

        plant.Touch();
        return action(plant);
    }

    private static bool TryParseAddress(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(text) || !IPAddress.TryParse(text, out IPAddress? parsed))
        {
            return false;
        }

        address = ModbusTcpServer.Normalize(parsed);
        return true;
    }

    private static IResult PairingRequestNotFound() => Results.NotFound(new ApiError(ApiErrorCodes.PairingRequestNotFound));

    private static ModbusInfoResponse ToResponse(PlantModbus modbus) => new(
        modbus.Port,
        modbus.UnitId,
        [.. modbus.Pairing.Authorized.Select(a => a.ToString())],
        [.. modbus.Pairing.Pending.Select(p => new PairingRequestResponse(p.Id, p.Address.ToString(), p.RequestedUtc, p.ExpiresUtc, p.WaitingConnections))],
        [.. modbus.Connections.Select(c => new ModbusConnectionResponse(
            c.Id,
            ModbusTcpServer.Normalize(c.Remote.Address).ToString(),
            c.Remote.Port,
            c.ConnectedUtc,
            c.Authorized,
            c.Requests))],
        modbus.Device.Requests,
        modbus.Device.ExceptionResponses);

    private static ModbusWriteResponse ToResponse(WriteEntry entry) => new(
        entry.Area == ModbusArea.Coil ? ModbusAreas.Coil : ModbusAreas.HoldingRegister,
        entry.Address,
        ModbusAddress.Classic(entry.Area, entry.Address),
        entry.Write.Author,
        entry.Write.TimeUtc);
}
