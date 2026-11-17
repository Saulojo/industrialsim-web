using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentModbus;
using IndustrialSim.Contracts;
using IndustrialSim.IO;
using IndustrialSim.Modbus.Tests;
using IndustrialSim.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// Modbus por planta com o servidor inteiro em memória, o laço de tempo real rodando e mestres de verdade em
/// loopback: API de pareamento e critérios 1 e 2 da Etapa 6.
/// </summary>
public class ModbusApiTests(ITestOutputHelper output)
{
    private static readonly IPAddress Other = IPAddress.Parse("127.0.0.2");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<PlantSummary> CreatePlant(HttpClient client)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<PlantSummary>(Cancel))!;
    }

    private static async Task<ModbusInfoResponse> GetModbus(HttpClient client, Guid plant) =>
        (await client.GetFromJsonAsync<ModbusInfoResponse>($"/api/plants/{plant}/modbus", Cancel))!;

    private static IPEndPoint EndPoint(PlantSummary plant) => new(IPAddress.Loopback, plant.ModbusPort);

    /// <summary>Pareamento pela API: pedido pendente, recusa, aceite, lista de autorizados e retirada.</summary>
    [Fact]
    public async Task Pairing_ThroughTheApi()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();
        PlantSummary plant = await CreatePlant(client);
        Assert.InRange(plant.ModbusPort, 15000, 15999);

        ModbusInfoResponse info = await GetModbus(client, plant.Id);
        Assert.Equal(plant.ModbusPort, info.Port);
        Assert.Equal(1, info.UnitId);
        Assert.Empty(info.AuthorizedMasters);
        Assert.Empty(info.PendingRequests);

        // 127.0.0.2 conecta: vira pedido pendente, e a recusa fecha a conexão.
        using RawModbusClient intruder = await RawModbusClient.ConnectAsync(EndPoint(plant), Other);
        PairingRequestResponse request = await WaitForPending(client, plant.Id);
        Assert.Equal("127.0.0.2", request.Address);
        Assert.False(Assert.Single((await GetModbus(client, plant.Id)).Connections).Authorized);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/plants/{plant.Id}/modbus/pairing/{request.Id}/refuse", null, Cancel)).StatusCode);
        Assert.True(await intruder.WasClosedByServerAsync());
        await AssertError(
            await client.PostAsync($"/api/plants/{plant.Id}/modbus/pairing/{request.Id}/accept", null, Cancel),
            HttpStatusCode.NotFound,
            ApiErrorCodes.PairingRequestNotFound);

        // 127.0.0.1 conecta e é aceito: passa a ser atendido e autorizado.
        using RawModbusClient master = await RawModbusClient.ConnectAsync(EndPoint(plant));
        request = await WaitForPending(client, plant.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/plants/{plant.Id}/modbus/pairing/{request.Id}/accept", null, Cancel)).StatusCode);
        Assert.Equal([0x06, 0x00, 0x00, 0x36, 0x00], await master.RequestAsync(1, 0x06, 0x00, 0x00, 0x36, 0x00));

        info = await GetModbus(client, plant.Id);
        Assert.Equal(["127.0.0.1"], info.AuthorizedMasters);
        ModbusConnectionResponse connection = Assert.Single(info.Connections);
        Assert.True(connection.Authorized);
        Assert.Equal("127.0.0.1", connection.Address);
        Assert.Equal(1, connection.Requests);

        ModbusWriteResponse write = Assert.Single((await client.GetFromJsonAsync<ModbusWriteResponse[]>($"/api/plants/{plant.Id}/modbus/writes", Cancel))!);
        Assert.Equal(new ModbusWriteResponse(ModbusAreas.HoldingRegister, 0, 40001, "127.0.0.1", write.TimeUtc), write);

        // Retirar a autorização fecha a conexão.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/plants/{plant.Id}/modbus/masters/127.0.0.1", Cancel)).StatusCode);
        Assert.True(await master.WasClosedByServerAsync());
        await AssertError(await client.DeleteAsync($"/api/plants/{plant.Id}/modbus/masters/127.0.0.1", Cancel), HttpStatusCode.NotFound, ApiErrorCodes.MasterNotFound);
        Assert.Empty((await GetModbus(client, plant.Id)).AuthorizedMasters);
    }

    [Fact]
    public async Task AuthorizingInAdvance_AndChangingTheUnitId()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();
        PlantSummary plant = await CreatePlant(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/plants/{plant.Id}/modbus/masters", new MasterAuthorizeRequest("127.0.0.1"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/plants/{plant.Id}/modbus/unit-id", new UnitIdRequest(255), Cancel)).StatusCode);

        using RawModbusClient master = await RawModbusClient.ConnectAsync(EndPoint(plant));
        Assert.Equal([0x84, 0x0B], await master.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x01));
        Assert.Equal(0x04, (await master.RequestAsync(255, 0x04, 0x00, 0x00, 0x00, 0x01))[0]);

        ModbusInfoResponse info = await GetModbus(client, plant.Id);
        Assert.Equal(255, info.UnitId);
        Assert.Equal(2, info.Requests);
        Assert.Equal(1, info.ExceptionResponses);
    }

    [Fact]
    public async Task InvalidModbusRequests_ReturnAnErrorCode()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();
        PlantSummary plant = await CreatePlant(client);
        string modbus = $"/api/plants/{plant.Id}/modbus";

        await AssertError(await client.PostAsJsonAsync($"{modbus}/masters", new MasterAuthorizeRequest("não é IP"), Cancel), HttpStatusCode.BadRequest, ApiErrorCodes.InvalidRequest);
        await AssertError(await client.DeleteAsync($"{modbus}/masters/abc", Cancel), HttpStatusCode.BadRequest, ApiErrorCodes.InvalidRequest);
        await AssertError(await client.PutAsJsonAsync($"{modbus}/unit-id", new UnitIdRequest(0), Cancel), HttpStatusCode.BadRequest, ApiErrorCodes.InvalidRequest);
        await AssertError(await client.PutAsJsonAsync($"{modbus}/unit-id", new UnitIdRequest(256), Cancel), HttpStatusCode.BadRequest, ApiErrorCodes.InvalidRequest);
        await AssertError(await client.PostAsync($"{modbus}/pairing/{Guid.NewGuid()}/refuse", null, Cancel), HttpStatusCode.NotFound, ApiErrorCodes.PairingRequestNotFound);
        await AssertError(await client.GetAsync($"/api/plants/{Guid.NewGuid()}/modbus", Cancel), HttpStatusCode.NotFound, ApiErrorCodes.PlantNotFound);
    }

    [Fact]
    public async Task WithoutAFreeModbusPort_CreatingAPlantReturnsAnErrorCode()
    {
        using var blocker = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        string busy = ((IPEndPoint)blocker.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var factory = new SimulationFactory(("Simulation:Modbus:PortRangeStart", busy), ("Simulation:Modbus:PortRangeEnd", busy));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel);

        await AssertError(response, HttpStatusCode.ServiceUnavailable, ApiErrorCodes.ModbusPortsExhausted);
    }

    /// <summary>
    /// Critério 1, de ponta a ponta: com a planta em tempo real, um mestre independente (FluentModbus) escreve
    /// "fechar" em FV-101 e a posição real publicada muda em até 2 passos depois do último passo visto antes da
    /// escrita; a leitura de LT-101 é coerente com a planta.
    /// </summary>
    [Fact]
    public async Task AMasterWritesFV101_ThePositionStartsMovingWithinTwoSteps_AndLT101IsCoherent()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();
        PlantSummary summary = await CreatePlant(client);
        await client.PostAsJsonAsync($"/api/plants/{summary.Id}/modbus/masters", new MasterAuthorizeRequest("127.0.0.1"), Cancel);
        Assert.True(factory.Services.GetRequiredService<InstanceManager>().TryGet(summary.Id, out PlantInstance? plant));
        using var master = new ModbusTcpClient { ReadTimeout = 5000, WriteTimeout = 5000 };
        master.Connect(EndPoint(summary), ModbusEndianness.BigEndian);
        await Task.Delay(200, Cancel);

        short positionBefore = master.ReadInputRegisters<short>(1, 4, 1)[0];
        long stepBeforeWrite = plant.Io.Image.Snapshot().StepCount;
        master.WriteSingleRegister(1, 0, (short)0);

        // Acompanha a imagem até a posição publicada mudar.
        IoSnapshot snapshot;
        var watch = Stopwatch.StartNew();
        do
        {
            snapshot = plant.Io.Image.Snapshot();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "A posição de FV-101 não mudou em 2 s.");
        }
        while (AnalogCodec.FromRegister(snapshot.InputRegisters[4]) >= positionBefore);

        long steps = snapshot.StepCount - stepBeforeWrite;
        output.WriteLine($"Posição publicada mudou {steps} passo(s) depois do último passo visto antes da escrita ({watch.Elapsed.TotalMilliseconds:F1} ms).");
        Assert.InRange(steps, 1, 2);
        Assert.True(master.ReadInputRegisters<short>(1, 4, 1)[0] < positionBefore);

        // LT-101 lido pelo mestre e a saída da planta no mesmo instante (entre dois passos), com até 1 contagem
        // de diferença; tenta de novo se um passo cair no meio das duas leituras.
        bool coherent = false;
        for (int attempt = 0; attempt < 20 && !coherent; attempt++)
        {
            long step = plant.Io.Image.Snapshot().StepCount;
            short counts = master.ReadInputRegisters<short>(1, 0, 1)[0];
            PlantState state = plant.GetState();
            if (state.StepCount != step)
            {
                continue;
            }

            double level = state.Outputs.Single(o => o.Name == "LT-101").Value;
            output.WriteLine($"LT-101: {counts} contagens lidas; planta em {level:F5} m = {27648.0 * level / 1.2:F1} contagens (passo {step}).");
            Assert.InRange(counts - (27648.0 * level / 1.2), -1.0, 1.0);
            coherent = true;
        }

        Assert.True(coherent, "Não foi possível ler LT-101 e a planta no mesmo passo.");
    }

    /// <summary>
    /// Critério 2: 30 plantas com 30 mestres simultâneos, cada um escrevendo valores distintos durante 3 s;
    /// nenhuma planta recebe valor de outra.
    /// </summary>
    [Fact]
    public async Task ThirtyPlantsWithThirtySimultaneousMasters_NoPlantReceivesAnotherPlantsValues()
    {
        const int Plants = 30;
        using var factory = new SimulationFactory(("Simulation:Instances:MaxInstances", "40"));
        using HttpClient client = factory.CreateClient();
        InstanceManager manager = factory.Services.GetRequiredService<InstanceManager>();

        var summaries = new PlantSummary[Plants];
        for (int i = 0; i < Plants; i++)
        {
            summaries[i] = await CreatePlant(client);
            await client.PostAsJsonAsync($"/api/plants/{summaries[i].Id}/modbus/masters", new MasterAuthorizeRequest("127.0.0.1"), Cancel);
        }

        Assert.Equal(Plants, summaries.Select(s => s.ModbusPort).Distinct().Count());

        // Valores distintos por planta: FV-101 e FV-102 em contagens, e o heartbeat com um prefixo da planta.
        static short WaterValve(int i) => (short)(1000 + (800 * i));
        static short FuelValve(int i) => (short)(27000 - (700 * i));

        var mismatches = new System.Collections.Concurrent.ConcurrentBag<string>();
        var writes = new long[Plants];
        DateTime stopAt = DateTime.UtcNow.AddSeconds(3);
        // Cada mestre (síncrono) numa thread própria, como 30 programas independentes, sem ocupar o pool.
        Task[] masters = [.. Enumerable.Range(0, Plants).Select(i => Task.Factory.StartNew(() =>
        {
            using var master = new ModbusTcpClient { ConnectTimeout = 5000, ReadTimeout = 5000, WriteTimeout = 5000 };
            master.Connect(EndPoint(summaries[i]), ModbusEndianness.BigEndian);
            ushort beat = 0;
            while (DateTime.UtcNow < stopAt)
            {
                master.WriteMultipleRegisters(1, 0, new[] { WaterValve(i), FuelValve(i) });
                master.WriteSingleRegister(1, 10, (ushort)((i << 10) | (beat++ & 0x3FF)));
                short[] back = master.ReadHoldingRegisters<short>(1, 0, 2).ToArray();
                if (back[0] != WaterValve(i) || back[1] != FuelValve(i))
                {
                    mismatches.Add($"planta {i}: leu {back[0]}, {back[1]}");
                }

                Interlocked.Increment(ref writes[i]);
            }
        }, Cancel, TaskCreationOptions.LongRunning, TaskScheduler.Default))];
        await Task.WhenAll(masters);

        // Deixa os comandos entrarem na planta.
        await Task.Delay(100, Cancel);

        Assert.Empty(mismatches);
        for (int i = 0; i < Plants; i++)
        {
            Assert.True(manager.TryGet(summaries[i].Id, out PlantInstance? plant));
            IoSnapshot snapshot = plant.Io.Image.Snapshot();
            Assert.Equal(WaterValve(i), AnalogCodec.FromRegister(snapshot.HoldingRegisters[0]));
            Assert.Equal(FuelValve(i), AnalogCodec.FromRegister(snapshot.HoldingRegisters[1]));
            Assert.Equal(i, snapshot.HoldingRegisters[10] >> 10);

            // A planta aplicou os próprios comandos (contagens → fração de abertura pela faixa 0–1).
            PlantState state = plant.GetState();
            Assert.Equal(WaterValve(i) / 27648.0, state.Inputs.Single(s => s.Name == "FV-101_command").Value, 9);
            Assert.Equal(FuelValve(i) / 27648.0, state.Inputs.Single(s => s.Name == "FV-102_command").Value, 9);
            Assert.Equal(writes[i] * 3, plant.Modbus.Device.Requests);
            Assert.Equal(0, plant.Modbus.Device.ExceptionResponses);
        }

        output.WriteLine($"Ciclos de escrita e leitura por mestre em 3 s: mínimo {writes.Min()}, máximo {writes.Max()}, total {writes.Sum()}.");
    }

    private static async Task<PairingRequestResponse> WaitForPending(HttpClient client, Guid plant)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            ModbusInfoResponse info = await GetModbus(client, plant);
            if (info.PendingRequests.Count == 1)
            {
                return info.PendingRequests[0];
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "O pedido de pareamento não apareceu.");
            await Task.Delay(10, Cancel);
        }
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>(Cancel))!.Code);
    }
}
