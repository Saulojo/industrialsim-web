using IndustrialSim.Contracts;
using IndustrialSim.Sessions;
using IndustrialSim.Sessions.RealTime;

namespace IndustrialSim.Server;

/// <summary>
/// API REST das plantas (Etapa 5): listar cenários, criar planta, listar, consultar estado, encerrar, baixar a
/// gravação do ensaio e ver as métricas do laço. O Modbus de cada planta fica em <see cref="ModbusApi"/>.
/// Erros devolvem só um código (<see cref="ApiError"/>).
/// </summary>
internal static class SimulationApi
{
    public static void MapSimulationApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/scenarios", (InstanceManager manager) => manager.Catalog.Scenarios.Select(ToSummary).ToArray());

        RouteGroupBuilder plants = app.MapGroup("/api/plants");
        plants.MapGet("/", (InstanceManager manager) => manager.List().Select(ToSummary).ToArray());
        plants.MapPost("/", Create);
        plants.MapGet("/{id:guid}", GetState);
        plants.MapDelete("/{id:guid}", End);
        plants.MapGet("/{id:guid}/recording", GetRecording);
        plants.MapModbusApi();
        plants.MapPlantSocket();

        app.MapGet("/api/simulation/metrics", (RealTimeLoop loop) => ToResponse(loop.GetMetrics()));
    }

    private static IResult Create(PlantCreateRequest? request, InstanceManager manager)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ScenarioId))
        {
            return Results.BadRequest(new ApiError(ApiErrorCodes.InvalidRequest));
        }

        CreatePlantResult result = manager.Create(new CreatePlantRequest(request.ScenarioId, request.NoiseSeed, OwnerId: null, request.Record));
        return result.Status switch
        {
            CreatePlantStatus.Created when result.Instance is { } instance =>
                Results.Created($"/api/plants/{instance.Id}", ToSummary(instance)),
            CreatePlantStatus.ScenarioNotFound => Results.BadRequest(new ApiError(ApiErrorCodes.ScenarioNotFound)),
            CreatePlantStatus.ModbusPortsExhausted =>
                Results.Json(new ApiError(ApiErrorCodes.ModbusPortsExhausted), statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(new ApiError(ApiErrorCodes.InstanceLimitReached), statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static IResult GetState(Guid id, InstanceManager manager)
    {
        if (!manager.TryGet(id, out PlantInstance? instance))
        {
            return PlantNotFound();
        }

        instance.Touch();
        PlantState state = instance.GetState();
        RealTimeTracking tracking = instance.Tracking;
        return Results.Ok(new PlantStateResponse(
            ToSummary(instance),
            state.StepCount,
            state.SimulatedSeconds,
            state.WatchdogTripped,
            instance.FaultMessage,
            [.. state.Inputs.Select(s => new SignalReading(s.Name, s.Value))],
            [.. state.Outputs.Select(s => new SignalReading(s.Name, s.Value))],
            new RealTimeStats(instance.LoopThreadIndex, tracking.WallSeconds, tracking.SimulatedSeconds, tracking.Drift)));
    }

    private static IResult End(Guid id, InstanceManager manager) =>
        manager.End(id) ? Results.NoContent() : PlantNotFound();

    private static IResult GetRecording(Guid id, InstanceManager manager)
    {
        if (!manager.TryGet(id, out PlantInstance? instance))
        {
            return PlantNotFound();
        }

        instance.Touch();
        if (instance.RecordingPath is not string path)
        {
            return Results.NotFound(new ApiError(ApiErrorCodes.RecordingNotFound));
        }

        // A gravação continua sendo escrita: lê o que já foi descarregado no disco.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Results.File(stream, "text/csv", $"{instance.ScenarioId}-{instance.Id:N}.csv");
    }

    internal static IResult PlantNotFound() => Results.NotFound(new ApiError(ApiErrorCodes.PlantNotFound));

    private static ScenarioSummary ToSummary(ScenarioEntry entry) => new(
        entry.Id,
        entry.Definition.StepSeconds,
        entry.Definition.NoiseSeed,
        [.. entry.Definition.Inputs.Select(i => i.Name)],
        [.. entry.Definition.Outputs.Select(o => o.Name)]);

    internal static string ToStatus(InstanceStatus status) => status switch
    {
        InstanceStatus.Running => PlantStatuses.Running,
        InstanceStatus.Faulted => PlantStatuses.Faulted,
        _ => PlantStatuses.Ended,
    };

    internal static PlantSummary ToSummary(PlantInstance instance) => new(
        instance.Id,
        instance.ScenarioId,
        instance.NoiseSeed,
        ToStatus(instance.Status),
        instance.CreatedUtc,
        instance.LastActivityUtc,
        instance.ClientCount,
        instance.RecordingPath is not null,
        instance.Modbus.Port);

    private static LoopMetricsResponse ToResponse(LoopMetrics metrics) => new(
        metrics.ThreadCount,
        metrics.PlantCount,
        metrics.WaiterKind,
        metrics.Cycles,
        metrics.Steps,
        metrics.LateCycles,
        metrics.DroppedCycles,
        metrics.Overruns,
        ToStats(metrics.StepLateness),
        ToStats(metrics.StepCost));

    private static DurationStats ToStats(DurationSummary summary) =>
        new(summary.Count, summary.MeanMs, summary.P50Ms, summary.P99Ms, summary.MaxMs);
}
