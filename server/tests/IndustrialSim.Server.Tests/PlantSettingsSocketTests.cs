using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using IndustrialSim.Contracts;
using IndustrialSim.Modbus.Tests;
using IndustrialSim.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// Etapa 8 pelo WebSocket, com o servidor inteiro em memória e o laço de tempo real rodando: configurações,
/// monitor de registradores, valores forçados, pausa e log de comunicação.
/// </summary>
public class PlantSettingsSocketTests(ITestOutputHelper output)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>
    /// Critério 1 pela tela: trocar o formato e o Unit ID pelo WebSocket muda o que o mestre lê, sem reiniciar
    /// a planta (o reinício e o tempo simulado continuam).
    /// </summary>
    [Fact]
    public async Task FormatAndUnitId_FromTheSocket_ChangeWhatTheMasterReads_WithoutRestarting()
    {
        using var factory = new SimulationFactory();
        (PlantSummary plant, PlantInstance instance) = await CreatePlant(factory);
        instance.Modbus.Pairing.Authorize(IPAddress.Loopback);
        using RawModbusClient master = await RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, plant.ModbusPort));
        await using PlantSocketClient socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();
        SettingsMessage initial = await socket.NextAsync<SettingsMessage>();
        Assert.Equal(new PlantSettingsInfo(AnalogFormats.Counts27648, WordOrders.HighWordFirst, false, 2.0, true, 1, MasterPolicies.Multiple), initial.Settings);
        StateMessage before = await socket.NextStateAsync();

        await socket.SendAsync(new UpdateSettingsCommand(Format: AnalogFormats.Float32));
        SettingsMessage changed = await socket.NextAsync<SettingsMessage>();
        Assert.Equal(AnalogFormats.Float32, changed.Settings.Format);
        StateMessage after = await socket.NextStateAsync(); // pelo menos um passo depois da troca

        // LT-101 no float32: registradores 0 e 1, em metros.
        byte[] reply = await master.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x02);
        float level = BitConverter.Int32BitsToSingle((reply[2] << 24) | (reply[3] << 16) | (reply[4] << 8) | reply[5]);
        output.WriteLine($"LT-101 lido em float32: {level} m");
        Assert.InRange(level, 0.3f, 0.9f);
        Assert.Equal(before.Run, after.Run);
        Assert.True(after.Time > before.Time);

        await socket.SendAsync(new UpdateSettingsCommand(UnitId: 42));
        await socket.NextAsync<SettingsMessage>(m => m.Settings.UnitId == 42);
        Assert.Equal([0x84, 0x0B], await master.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x02));
        Assert.Equal(6, (await master.RequestAsync(42, 0x04, 0x00, 0x00, 0x00, 0x02)).Length);
        Assert.Equal(42, (await socket.NextAsync<ModbusStatusMessage>(m => m.UnitId == 42)).UnitId);
    }

    /// <summary>Configurações inválidas ou impossíveis voltam um erro, e nada muda.</summary>
    [Fact]
    public async Task InvalidSettings_AreRefusedWithACode()
    {
        using var factory = new SimulationFactory();
        (PlantSummary plant, PlantInstance instance) = await CreatePlant(factory);
        await using PlantSocketClient socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();

        await socket.SendAsync(new UpdateSettingsCommand(Format: "0-20mA"));
        Assert.Equal(SocketErrorCodes.InvalidSettings, (await socket.NextAsync<SocketErrorMessage>()).Code);
        await socket.SendAsync(new UpdateSettingsCommand(UnitId: 256));
        Assert.Equal(SocketErrorCodes.InvalidSettings, (await socket.NextAsync<SocketErrorMessage>()).Code);

        instance.Modbus.Pairing.Authorize(IPAddress.Parse("10.0.0.1"));
        instance.Modbus.Pairing.Authorize(IPAddress.Parse("10.0.0.2"));
        await socket.SendAsync(new UpdateSettingsCommand(MasterPolicy: MasterPolicies.Single));
        Assert.Equal(SocketErrorCodes.SeveralMastersAuthorized, (await socket.NextAsync<SocketErrorMessage>()).Code);

        await socket.SendAsync(new RevokeMasterCommand("10.0.0.2"));
        await socket.SendAsync(new UpdateSettingsCommand(MasterPolicy: MasterPolicies.Single));
        Assert.Equal(MasterPolicies.Single, (await socket.NextAsync<SettingsMessage>(m => m.Settings.MasterPolicy == MasterPolicies.Single)).Settings.MasterPolicy);
        await socket.SendAsync(new RevokeMasterCommand("10.0.0.2"));
        Assert.Equal(SocketErrorCodes.MasterNotFound, (await socket.NextAsync<SocketErrorMessage>()).Code);
    }

    /// <summary>
    /// Critério 2: a escrita de um mestre aparece no monitor em até 200 ms, com o IP de origem. Vinte escritas,
    /// cada uma medida do envio até a mensagem do monitor que traz o valor.
    /// </summary>
    [Fact]
    public async Task AMasterWrite_ShowsUpInTheMonitor_Within200ms_WithItsAddress()
    {
        using var factory = new SimulationFactory();
        (PlantSummary plant, PlantInstance instance) = await CreatePlant(factory);
        instance.Modbus.Pairing.Authorize(IPAddress.Loopback);
        using RawModbusClient master = await RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, plant.ModbusPort));
        await using PlantSocketClient socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();
        await socket.SendAsync(new SubscribeMonitorCommand(true));
        MonitorLayoutMessage layout = await socket.NextAsync<MonitorLayoutMessage>();
        int point = layout.Points.ToList().FindIndex(p => p.Area == ModbusAreas.HoldingRegister && p.Signal == "FV-101_command");
        int rawIndex = layout.Points.Take(point).Sum(p => p.Length);
        Assert.Equal(new MonitorPointInfo(ModbusAreas.HoldingRegister, 0, 40001, 1, "FV-101_command", "%", MonitorPointKinds.Analog), layout.Points[point]);

        var delays = new List<double>();
        string? author = null;
        for (int i = 0; i < 20; i++)
        {
            ushort value = (ushort)(10000 + (i * 500));
            var watch = Stopwatch.StartNew();
            await master.RequestAsync(1, 0x06, 0x00, 0x00, (byte)(value >> 8), (byte)value);
            MonitorMessage seen = await socket.NextAsync<MonitorMessage>(m =>
            {
                author = m.Writes?.FirstOrDefault(w => w.Point == point)?.Author ?? author;
                return m.Raw[rawIndex] == value && author is not null;
            });
            delays.Add(watch.Elapsed.TotalMilliseconds);
            Assert.Equal(value / 27648.0 * 100.0, seen.Values[point], 0.01);
        }

        delays.Sort();
        output.WriteLine($"Escrita → monitor: mín {delays[0]:F0} ms, mediana {delays[10]:F0} ms, máx {delays[^1]:F0} ms");
        Assert.Equal("127.0.0.1", author);
        Assert.True(delays[^1] <= 200, $"A escrita levou {delays[^1]:F0} ms para aparecer no monitor.");
    }

    /// <summary>
    /// Critério 3: um valor forçado pelo WebSocket aparece marcado (na lista de forçados e no monitor), vale
    /// para o mestre e deixa de valer ao ser liberado.
    /// </summary>
    [Fact]
    public async Task AForcedValue_IsFlagged_AndStopsWhenReleased()
    {
        using var factory = new SimulationFactory();
        (PlantSummary plant, PlantInstance instance) = await CreatePlant(factory);
        instance.Modbus.Pairing.Authorize(IPAddress.Loopback);
        using RawModbusClient master = await RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, plant.ModbusPort));
        await using PlantSocketClient socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();
        Assert.Empty((await socket.NextAsync<ForcedMessage>()).Inputs);
        await socket.SendAsync(new SubscribeMonitorCommand(true));
        MonitorLayoutMessage layout = await socket.NextAsync<MonitorLayoutMessage>();
        int point = layout.Points.ToList().FindIndex(p => p.Area == ModbusAreas.InputRegister && p.Signal == "LT-101");

        await socket.SendAsync(new ForceInputCommand(ModbusAreas.InputRegister, "LT-101", 0.3));
        Assert.Equal([new ForcedInputInfo(ModbusAreas.InputRegister, "LT-101", 0.3)], (await socket.NextAsync<ForcedMessage>()).Inputs);
        MonitorMessage forced = await socket.NextAsync<MonitorMessage>(m => m.Forced.Contains(point));
        Assert.Equal(0.3f, forced.Values[point], 0.0001f);
        Assert.Equal([0x04, 0x02, 0x1B, 0x00], await master.RequestAsync(1, 0x04, 0x00, 0x00, 0x00, 0x01)); // 6912 = 25 %

        await socket.SendAsync(new ForceInputCommand(ModbusAreas.InputRegister, "não-existe", 1.0));
        Assert.Equal(SocketErrorCodes.InputNotFound, (await socket.NextAsync<SocketErrorMessage>()).Code);

        await socket.SendAsync(new ReleaseInputCommand(ModbusAreas.InputRegister, "LT-101"));
        Assert.Empty((await socket.NextAsync<ForcedMessage>()).Inputs);
        MonitorMessage released = await socket.NextAsync<MonitorMessage>(m => !m.Forced.Contains(point));
        Assert.InRange(released.Values[point], 0.4f, 0.8f);
    }

    /// <summary>
    /// Critério 5 pela tela: pausar congela a planta (o estado chega como "pausada", com o tempo parado) e
    /// retomar continua do mesmo ponto.
    /// </summary>
    [Fact]
    public async Task PauseAndResume_ThroughTheSocket()
    {
        using var factory = new SimulationFactory();
        (PlantSummary plant, _) = await CreatePlant(factory);
        await using PlantSocketClient socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();

        await socket.SendAsync(new PauseCommand());
        StateMessage paused = await socket.NextStateAsync(s => s.Status == PlantStatuses.Paused);
        StateMessage later = await socket.NextStateAsync(s => s.Status == PlantStatuses.Paused && s != paused);
        await Task.Delay(500, Cancel);
        StateMessage stillPaused = await socket.NextStateAsync();
        Assert.Equal(paused.Step, stillPaused.Step);
        Assert.Equal(paused.Time, later.Time);
        Assert.Equal(PlantStatuses.Paused, stillPaused.Status);

        await socket.SendAsync(new PauseCommand());
        Assert.Equal(SocketErrorCodes.PlantNotRunning, (await socket.NextAsync<SocketErrorMessage>()).Code);

        await socket.SendAsync(new ResumeCommand());
        StateMessage resumed = await socket.NextStateAsync(s => s.Status == PlantStatuses.Running && s.Step > paused.Step);
        Assert.Equal(paused.Run, resumed.Run);
        Assert.InRange(resumed.Step - paused.Step, 1, 20);
        await socket.SendAsync(new ResumeCommand());
        Assert.Equal(SocketErrorCodes.PlantNotPaused, (await socket.NextAsync<SocketErrorMessage>()).Code);

        using HttpClient client = factory.CreateClient();
        PlantSummary[] list = (await client.GetFromJsonAsync<PlantSummary[]>("/api/plants", Cancel))!;
        Assert.Equal(PlantStatuses.Running, Assert.Single(list).Status);
    }

    /// <summary>Critério 6 pela tela: cada configuração mudada pelo WebSocket chega no log, uma linha por mudança.</summary>
    [Fact]
    public async Task EverySettingChangedThroughTheSocket_ArrivesInTheLog()
    {
        using var factory = new SimulationFactory();
        (PlantSummary plant, _) = await CreatePlant(factory);
        await using PlantSocketClient socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();
        LogMessage initial = await socket.NextAsync<LogMessage>();
        Assert.True(initial.Reset);
        Assert.Empty(initial.Entries);

        await socket.SendAsync(new UpdateSettingsCommand(
            AnalogFormats.Counts32767, WordOrders.LowWordFirst, true, 3.5, false, 7, MasterPolicies.Single));
        var entries = new List<LogEntryInfo>();
        while (entries.Count < 7)
        {
            LogMessage message = await socket.NextAsync<LogMessage>();
            Assert.False(message.Reset);
            entries.AddRange(message.Entries);
        }

        Assert.All(entries, e => Assert.Equal(LogEventKinds.SettingChanged, e.Kind));
        Assert.Equal(
            [
                ("masterPolicy", "multiple", "single"),
                ("format", "counts27648", "counts32767"),
                ("wordOrder", "highWordFirst", "lowWordFirst"),
                ("watchdogEnabled", "false", "true"),
                ("watchdogTimeoutSeconds", "2", "3.5"),
                ("diagnosticsEnabled", "true", "false"),
                ("unitId", "1", "7"),
            ],
            entries.Select(e => (Arg(e, "setting"), Arg(e, "old"), Arg(e, "new"))));
    }

    /// <summary>
    /// Critério 4: o mapa exportado em CSV bate com o <c>scenario.json</c>, lido aqui de forma independente
    /// (sem o carregador do servidor), nos quatro formatos.
    /// </summary>
    [Theory]
    [InlineData(AnalogFormats.Counts27648)]
    [InlineData(AnalogFormats.Counts10000)]
    [InlineData(AnalogFormats.Counts32767)]
    [InlineData(AnalogFormats.Float32)]
    public async Task TheExportedIoMap_MatchesTheScenarioFile(string format)
    {
        using var factory = new SimulationFactory();
        (PlantSummary plant, PlantInstance instance) = await CreatePlant(factory);
        Assert.True(SettingText.TryParse(format, out IO.AnalogFormat parsed));
        instance.UpdateSettings(new PlantSettingsUpdate(Format: parsed));

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync($"/api/plants/{plant.Id}/io-map.csv", Cancel);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"caldeira-io-map-{format}.csv", response.Content.Headers.ContentDisposition?.FileName);
        string csv = await response.Content.ReadAsStringAsync(Cancel);
        output.WriteLine(csv);

        string[] lines = csv.TrimEnd('\n').Split('\n');
        Assert.Equal(IoMapApi.CsvHeader, lines[0]);
        string scenario = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scenarios", "caldeira", "scenario.json"), Cancel);
        Assert.Equal(ExpectedIoMap.FromScenario(scenario, format), lines[1..]);

        IoMapResponse json = (await client.GetFromJsonAsync<IoMapResponse>($"/api/plants/{plant.Id}/io-map", Cancel))!;
        Assert.Equal(format, json.Format);
        Assert.Equal(lines.Length - 1, json.Rows.Count);
    }

    private static string? Arg(LogEntryInfo entry, string name) => entry.Arguments.FirstOrDefault(a => a.Name == name)?.Value;

    private static async Task<(PlantSummary Plant, PlantInstance Instance)> CreatePlant(SimulationFactory factory)
    {
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        PlantSummary plant = (await created.Content.ReadFromJsonAsync<PlantSummary>(Cancel))!;
        Assert.True(factory.Services.GetRequiredService<InstanceManager>().TryGet(plant.Id, out PlantInstance? instance));
        return (plant, instance);
    }
}
