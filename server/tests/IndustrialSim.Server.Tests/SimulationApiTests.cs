using System.Net;
using System.Net.Http.Json;
using IndustrialSim.Contracts;
using IndustrialSim.Sessions;
using IndustrialSim.Sessions.RealTime;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Server.Tests;

/// <summary>API REST das plantas (Etapa 5), com o servidor inteiro em memória e o laço de tempo real rodando.</summary>
public class SimulationApiTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scenarios_ListsTheBoiler()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();

        ScenarioSummary[]? scenarios = await client.GetFromJsonAsync<ScenarioSummary[]>("/api/scenarios", Cancel);

        ScenarioSummary caldeira = Assert.Single(scenarios!, s => s.Id == "caldeira");
        Assert.Equal(0.01, caldeira.StepSeconds);
        Assert.Equal(101UL, caldeira.NoiseSeed);
        Assert.Contains("FV-101_command", caldeira.Inputs);
        Assert.Contains("LT-101", caldeira.Outputs);
    }

    [Fact]
    public async Task CreateQueryAndEnd_APlant()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();

        // Criar
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira", NoiseSeed: 5), Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        PlantSummary? plant = await created.Content.ReadFromJsonAsync<PlantSummary>(Cancel);
        Assert.NotNull(plant);
        Assert.Equal($"/api/plants/{plant.Id}", created.Headers.Location?.OriginalString);
        Assert.Equal("caldeira", plant.ScenarioId);
        Assert.Equal(5UL, plant.NoiseSeed);
        Assert.Equal(PlantStatuses.Running, plant.Status);
        Assert.False(plant.Recording);

        // Listar
        PlantSummary[]? list = await client.GetFromJsonAsync<PlantSummary[]>("/api/plants", Cancel);
        Assert.Equal(plant.Id, Assert.Single(list!).Id);

        // Consultar: a planta avança em tempo real.
        await Task.Delay(TimeSpan.FromMilliseconds(300), Cancel);
        PlantStateResponse? state = await client.GetFromJsonAsync<PlantStateResponse>($"/api/plants/{plant.Id}", Cancel);
        Assert.NotNull(state);
        Assert.True(state.StepCount > 10, $"Só {state.StepCount} passos em 300 ms.");
        Assert.Equal(state.StepCount * 0.01, state.SimulatedSeconds, 9);
        Assert.False(state.WatchdogTripped);
        Assert.Null(state.FaultMessage);
        Assert.Contains(state.Outputs, o => o.Name == "LT-101" && o.Value > 0.0);
        Assert.Equal(0.8228, state.Inputs.Single(i => i.Name == "FV-101_command").Value, 3);
        Assert.InRange(state.RealTime.LoopThread, 0, 1);
        Assert.True(state.RealTime.WallSeconds > 0.0);

        // Encerrar
        HttpResponseMessage deleted = await client.DeleteAsync($"/api/plants/{plant.Id}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        HttpResponseMessage gone = await client.GetAsync($"/api/plants/{plant.Id}", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(ApiErrorCodes.PlantNotFound, (await gone.Content.ReadFromJsonAsync<ApiError>(Cancel))!.Code);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/plants/{plant.Id}", Cancel)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<PlantSummary[]>("/api/plants", Cancel))!);
    }

    [Fact]
    public async Task InvalidRequests_ReturnAnErrorCode()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage unknown = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("nao-existe"), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(ApiErrorCodes.ScenarioNotFound, (await unknown.Content.ReadFromJsonAsync<ApiError>(Cancel))!.Code);

        HttpResponseMessage empty = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest(" "), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(ApiErrorCodes.InvalidRequest, (await empty.Content.ReadFromJsonAsync<ApiError>(Cancel))!.Code);

        HttpResponseMessage notAGuid = await client.GetAsync("/api/plants/123", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, notAGuid.StatusCode);
    }

    [Fact]
    public async Task TheInstanceLimit_Returns503()
    {
        using var factory = new SimulationFactory(("Simulation:Instances:MaxInstances", "2"));
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel)).StatusCode);
        HttpResponseMessage third = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, third.StatusCode);
        Assert.Equal(ApiErrorCodes.InstanceLimitReached, (await third.Content.ReadFromJsonAsync<ApiError>(Cancel))!.Code);
    }

    [Fact]
    public async Task TheRecording_CanBeDownloadedAsCsv()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();
        PlantSummary recorded = await Create(client, new PlantCreateRequest("caldeira", Record: true));
        PlantSummary notRecorded = await Create(client, new PlantCreateRequest("caldeira"));
        Assert.True(recorded.Recording);
        await Task.Delay(TimeSpan.FromMilliseconds(500), Cancel);

        HttpResponseMessage response = await client.GetAsync($"/api/plants/{recorded.Id}/recording", Cancel);
        string csv = await response.Content.ReadAsStringAsync(Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("#format=industrialsim-recording/1\n#scenario=caldeira\n", csv, StringComparison.Ordinal);
        Assert.Contains("\nstep,time_s,", csv, StringComparison.Ordinal);
        Assert.Contains("\n1,0.01,", csv, StringComparison.Ordinal);

        HttpResponseMessage none = await client.GetAsync($"/api/plants/{notRecorded.Id}/recording", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(ApiErrorCodes.RecordingNotFound, (await none.Content.ReadFromJsonAsync<ApiError>(Cancel))!.Code);
    }

    [Fact]
    public async Task Metrics_ShowTheLoop()
    {
        using var factory = new SimulationFactory(("Simulation:Loop:ThreadCount", "3"));
        using HttpClient client = factory.CreateClient();
        await Create(client, new PlantCreateRequest("caldeira"));
        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancel);

        LoopMetricsResponse? metrics = await client.GetFromJsonAsync<LoopMetricsResponse>("/api/simulation/metrics", Cancel);

        Assert.NotNull(metrics);
        Assert.Equal(3, metrics.ThreadCount);
        Assert.Equal(1, metrics.PlantCount);
        Assert.True(metrics.Steps > 0);
        // O atraso é registrado antes do passo e o contador de passos sobe depois dele; as métricas leem os
        // contadores antes dos histogramas. Com o laço rodando, o histograma pode estar alguns passos à frente.
        Assert.InRange(metrics.StepLateness.Count, metrics.Steps, metrics.Steps + 100);
        Assert.NotEqual("not-started", metrics.WaiterKind);
    }

    [Fact]
    public async Task AnIdlePlant_ExpiresAndLeavesTheList()
    {
        using var factory = new SimulationFactory(
            ("Simulation:Instances:IdleTimeout", "00:00:00.3"),
            ("Simulation:Instances:ExpiryCheckInterval", "00:00:00.05"));
        using HttpClient client = factory.CreateClient();
        await Create(client, new PlantCreateRequest("caldeira"));

        PlantSummary[]? list = null;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(100, Cancel);
            list = await client.GetFromJsonAsync<PlantSummary[]>("/api/plants", Cancel);
            if (list!.Length == 0)
            {
                break;
            }
        }

        Assert.Empty(list!);

        // A planta encerrada não dá mais passos, mas a thread do laço só a tira da sua lista no ciclo seguinte.
        RealTimeLoop loop = factory.Services.GetRequiredService<RealTimeLoop>();
        for (int i = 0; i < 50 && loop.GetMetrics().PlantCount > 0; i++)
        {
            await Task.Delay(20, Cancel);
        }

        Assert.Equal(0, loop.GetMetrics().PlantCount);
    }

    [Fact]
    public async Task StoppingTheHost_WithActivePlants_EndsThemAndLeavesNoLoopThreads()
    {
        var factory = new SimulationFactory();
        HttpClient client = factory.CreateClient();
        for (int i = 0; i < 3; i++)
        {
            await Create(client, new PlantCreateRequest("caldeira", Record: true));
        }

        RealTimeLoop loop = factory.Services.GetRequiredService<RealTimeLoop>();
        InstanceManager manager = factory.Services.GetRequiredService<InstanceManager>();
        PlantInstance[] plants = [.. manager.List()];
        Assert.Equal(2, loop.AliveThreadCount);

        client.Dispose();
        await factory.DisposeAsync();

        Assert.Equal(0, loop.AliveThreadCount);
        Assert.Equal(0, manager.Count);
        Assert.All(plants, p => Assert.Equal(EndReason.Shutdown, p.EndReason));
    }

    private static async Task<PlantSummary> Create(HttpClient client, PlantCreateRequest request)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/plants", request, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlantSummary>(Cancel))!;
    }
}
