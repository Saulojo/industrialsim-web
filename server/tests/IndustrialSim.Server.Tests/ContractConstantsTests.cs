using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using IndustrialSim.Contracts;
using IndustrialSim.IO;
using IndustrialSim.Modbus.Tests;
using IndustrialSim.Sessions;
using IndustrialSim.Sessions.Modbus;

namespace IndustrialSim.Server.Tests;

/// <summary>
/// Etapa 8: os textos estáveis que o servidor grava no log e nas configurações são os mesmos do contrato (a IHM
/// os traduz), e o monitor ligado cabe na banda por cliente.
/// </summary>
public class ContractConstantsTests(ITestOutputHelper output)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void LogKinds_AreTheSameOnBothSides() =>
        Assert.Equal(Constants(typeof(PlantEventKinds)), Constants(typeof(LogEventKinds)));

    [Fact]
    public void SettingValues_AreTheSameOnBothSides()
    {
        Assert.Equal(Enum.GetValues<AnalogFormat>().Select(SettingText.Of).Order(), Constants(typeof(AnalogFormats)).Order());
        Assert.Equal(Enum.GetValues<WordOrder>().Select(SettingText.Of).Order(), Constants(typeof(WordOrders)).Order());
        Assert.Equal(Enum.GetValues<MasterPolicy>().Select(SettingText.Of).Order(), Constants(typeof(MasterPolicies)).Order());
        Assert.Equal(
            Enum.GetValues<ModbusArea>().Select(SettingText.OfArea).Order(),
            Constants(typeof(ModbusAreas)).Order());
    }

    /// <summary>
    /// Banda de uma IHM com o monitor ligado e um mestre escrevendo a 10 Hz: estado a 20 Hz, monitor a 10 Hz e o
    /// resto quando muda; continua abaixo de 100 kbit/s (meta da Etapa 7). O monitor nunca passa de 10 Hz (com
    /// folga de medida); o mínimo não é conferido, porque numa VM compartilhada do CI o laço pode atrasar.
    /// </summary>
    [Fact]
    public async Task WithTheMonitorOn_TheBandwidthStaysUnder100kbps()
    {
        using var factory = new SimulationFactory();
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/plants", new PlantCreateRequest("caldeira"), Cancel);
        PlantSummary plant = (await created.Content.ReadFromJsonAsync<PlantSummary>(Cancel))!;
        await using PlantSocketClient socket = await PlantSocketClient.ConnectAsync(factory, plant.Id);
        await socket.HelloAsync();
        await client.PostAsJsonAsync($"/api/plants/{plant.Id}/modbus/masters", new MasterAuthorizeRequest("127.0.0.1"), Cancel);
        using RawModbusClient master = await RawModbusClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, plant.ModbusPort));
        await socket.SendAsync(new SubscribeMonitorCommand(true));
        await socket.NextAsync<MonitorMessage>();

        socket.ResetCounters();
        var watch = Stopwatch.StartNew();
        int monitors = 0;
        Task writer = Task.Run(async () =>
        {
            for (ushort i = 0; watch.Elapsed < TimeSpan.FromSeconds(5); i++)
            {
                await master.RequestAsync(1, 0x06, 0x00, 0x0A, (byte)(i >> 8), (byte)i); // heartbeat
                await Task.Delay(100, Cancel);
            }
        }, Cancel);
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (await socket.ReceiveAsync() is MonitorMessage)
            {
                monitors++;
            }
        }

        await writer;
        double seconds = watch.Elapsed.TotalSeconds;
        double kbitPerSecond = socket.BytesReceived * 8 / 1000.0 / seconds;
        output.WriteLine($"{monitors / seconds:F1} mensagens do monitor por segundo; {kbitPerSecond:F1} kbit/s no total");
        Assert.InRange(monitors / seconds, 1, 12);
        Assert.True(kbitPerSecond <= 100, $"banda de {kbitPerSecond:F1} kbit/s");
    }

    private static string[] Constants(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];
}
