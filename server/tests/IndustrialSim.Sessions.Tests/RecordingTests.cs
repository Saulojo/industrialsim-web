using System.Security.Cryptography;
using System.Text;
using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;
using IndustrialSim.Sessions.RealTime;
using IndustrialSim.Sessions.Recording;

namespace IndustrialSim.Sessions.Tests;

/// <summary>Gravação de ensaios e reprodução (RF07): reproduzir gera o mesmo CSV, byte a byte.</summary>
public sealed class RecordingTests : IDisposable
{
    // Endereços do mapa da caldeira.
    private const int PumpCoil = 0;
    private const int WaterValve = 0;
    private const int FuelValve = 1;
    private const int Heartbeat = 10;

    private static readonly ScenarioCatalog Catalog = ProductScenarios.Catalog();
    private static readonly ScenarioDefinition Caldeira = Catalog.Scenarios.Single(s => s.Id == "caldeira").Definition;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "industrialsim-recording-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Replay_OfAScriptedTest_ProducesTheSameCsv()
    {
        // Ensaio feito passo a passo, com tudo o que muda a planta: comandos, formato, watchdog e um distúrbio.
        var io = new PlantIo(ScenarioPlant.Create(Caldeira, noiseSeed: 2026));
        var recorded = new StringBuilder();
        var recorder = new PlantRecorder(io, new RecordingHeader("caldeira", 2026, 0.01, 10), line => recorded.Append(line).Append('\n'));

        ushort heartbeat = 0;
        for (int n = 0; n < 3000; n++)
        {
            if (n < 1500 || n >= 2000)
            {
                Assert.True(io.Image.TryWriteHoldingRegister(Heartbeat, heartbeat++));
            }

            switch (n)
            {
                case 100:
                    io.Image.TryWriteHoldingRegister(WaterValve, AnalogCodec.ToRegister(13824));
                    break;
                case 300:
                    io.Image.TryWriteCoil(PumpCoil, false);
                    break;
                case 400:
                    io.Image.TryWriteCoil(PumpCoil, true);
                    break;
                case 500:
                    io.Plant.SetInput("FV-103_command", 0.8); // distúrbio: mais vapor
                    break;
                case 1000:
                    io.UpdateSettings(io.Settings with { Format = AnalogFormat.Float32, WordOrder = WordOrder.LowWordFirst });
                    break;
                case 1200:
                    WriteFloat(io, FuelValve, 70.0f, WordOrder.LowWordFirst);
                    break;
                case 1400:
                    io.UpdateSettings(io.Settings with { WatchdogEnabled = true });
                    break;
            }

            io.Step();
            recorder.OnStepCompleted();
        }

        Assert.True(io.Plant.Clock.StepCount == 3000);
        string original = recorded.ToString();

        var replayed = new StringWriter { NewLine = "\n" };
        ReplayResult result = Replayer.Replay(new StringReader(original), Caldeira, replayed);

        Assert.Equal(3000, result.Steps);
        Assert.Equal(recorder.RowsWritten, result.RowsWritten);
        Assert.Equal(Sha256(original), Sha256(replayed.ToString()));
        Assert.Equal(original, replayed.ToString());

        // O ensaio passou pelo watchdog: há linhas com ele disparado e com ele recuperado.
        string[] rows = DataRows(original);
        Assert.Contains(rows, r => r.EndsWith(",1", StringComparison.Ordinal));
        Assert.EndsWith(",0", rows[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheCsv_HasTheHeader_AndOneRowPerSampleOrChange()
    {
        var io = new PlantIo(ScenarioPlant.Create(Caldeira));
        var lines = new List<string>();
        _ = new PlantRecorder(io, new RecordingHeader("caldeira", 101, 0.01, 10), lines.Add);
        var recorder = (PlantRecorder)io.LatchObserver!;

        for (int n = 0; n < 50; n++)
        {
            if (n == 23)
            {
                io.Image.TryWriteHoldingRegister(WaterValve, AnalogCodec.ToRegister(0));
            }

            io.Step();
            recorder.OnStepCompleted();
        }

        Assert.Equal(
            ["#format=industrialsim-recording/1", "#scenario=caldeira", "#noise_seed=101", "#step_s=0.01", "#sample_every_steps=10"],
            lines.Take(5));
        string[] columns = lines[5].Split(',');
        Assert.Equal(["step", "time_s", "coil_0", "hr_0", "hr_1"], columns.Take(5));
        Assert.Contains("format", columns);
        Assert.Contains("FV-103_command", columns);
        Assert.Contains("LT-101", columns);
        Assert.Equal("watchdog_tripped", columns[^1]);

        // Passo 1 (o primeiro), amostras a cada 10 passos e o passo 24 (comando novo, valeu no passo 23→24).
        long[] steps = [.. lines.Skip(6).Select(l => long.Parse(l.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture))];
        Assert.Equal([1, 10, 20, 24, 30, 40, 50], steps);
        Assert.StartsWith("24,0.24,", lines[6 + 3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARealTimeRecording_WithCommandsFromAnotherThread_ReplaysToTheSameHash()
    {
        string path;
        using (var loop = new RealTimeLoop())
        using (var manager = new InstanceManager(Catalog, loop, new InstanceManagerOptions { RecordingDirectory = _directory }))
        {
            loop.Start();
            CreatePlantResult result = manager.Create(new CreatePlantRequest("caldeira", NoiseSeed: 42, Record: true));
            PlantInstance plant = Assert.IsType<PlantInstance>(result.Instance);
            path = Assert.IsType<string>(plant.RecordingPath);

            // Um "mestre" escreve em momentos aleatórios, sem sincronia com o passo. Conta escritas, e não
            // tempo, para o ensaio ter o mesmo tamanho numa máquina lenta (o CI).
            var random = new Random(7);
            ushort heartbeat = 0;
            int writes = 0;
            while (writes < 80)
            {
                plant.Io.Image.TryWriteHoldingRegister(Heartbeat, heartbeat++);
                plant.Io.Image.TryWriteHoldingRegister(WaterValve, AnalogCodec.ToRegister((short)random.Next(0, 27649)));
                plant.Io.Image.TryWriteCoil(PumpCoil, random.Next(4) != 0);
                if (writes == 40)
                {
                    plant.Io.UpdateSettings(plant.Io.Settings with { Format = AnalogFormat.Counts10000 });
                }

                writes++;
                await Task.Delay(random.Next(1, 25), TestContext.Current.CancellationToken);
            }

            Assert.True(plant.GetState().StepCount > 100);
            manager.End(plant.Id);
        }

        string original = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var replayed = new StringWriter { NewLine = "\n" };
        using (var reader = new StreamReader(path))
        {
            Replayer.Replay(reader, Caldeira, replayed);
        }

        // Linhas fora da amostragem de 10 em 10 passos são de comandos que mudaram no meio do ensaio.
        string[] rows = DataRows(original);
        int changeRows = rows.Count(r => long.Parse(r[..r.IndexOf(',', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture) % 10 != 0);
        Assert.True(changeRows >= 20, $"Só {changeRows} linhas de mudança em {rows.Length} linhas.");
        Assert.Equal(Sha256(original), Sha256(replayed.ToString()));
    }

    [Fact]
    public void AnotherScenario_OrACorruptedRow_IsRejected()
    {
        var io = new PlantIo(ScenarioPlant.Create(Caldeira));
        var text = new StringBuilder();
        var recorder = new PlantRecorder(io, new RecordingHeader("caldeira", 101, 0.01, 10), line => text.Append(line).Append('\n'));
        for (int n = 0; n < 20; n++)
        {
            io.Step();
            recorder.OnStepCompleted();
        }

        string good = text.ToString();
        ScenarioDefinition other = Caldeira with { Id = "outra" };
        var wrongScenario = Assert.Throws<FormatException>(() => Replayer.Replay(new StringReader(good), other, TextWriter.Null));
        Assert.Contains("caldeira", wrongScenario.Message, StringComparison.Ordinal);

        string corrupted = good.Replace("\n10,0.1,1,", "\n10,0.1,x,", StringComparison.Ordinal);
        Assert.NotEqual(good, corrupted);
        var badRow = Assert.Throws<FormatException>(() => Replayer.Replay(new StringReader(corrupted), Caldeira, TextWriter.Null));
        Assert.Contains("linha 8", badRow.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecorderMustStartBeforeTheFirstStep()
    {
        var io = new PlantIo(ScenarioPlant.Create(Caldeira));
        io.Step();

        Assert.Throws<ArgumentException>(() => new PlantRecorder(io, new RecordingHeader("caldeira", 101, 0.01, 10), _ => { }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static void WriteFloat(PlantIo io, int pointAddress, float value, WordOrder order)
    {
        var words = new ushort[2];
        AnalogCodec.WriteFloat32(value, order, words);
        Assert.True(io.Image.TryWriteHoldingRegisters(IoMap.RegisterAddress(pointAddress, AnalogFormat.Float32), words));
    }

    private static string[] DataRows(string csv) =>
        [.. csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).Skip(1)];

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
