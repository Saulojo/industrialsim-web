using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;
using IndustrialSim.Sessions.Recording;

namespace IndustrialSim.SimRun.Tests;

/// <summary><c>simrun reproduzir</c>: reproduz uma gravação e compara os SHA-256.</summary>
public sealed class ReplayCommandTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("simrun-replay-").FullName;

    private static string Caldeira => Path.Combine(AppContext.BaseDirectory, "ProductScenarios", "caldeira", "scenario.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Replay_OfARecording_GivesAnIdenticalFile()
    {
        string recording = Record(steps: 500);
        string copy = Path.Combine(_folder, "copia.csv");

        var (exit, stdout, stderr) = Invoke("reproduzir", recording, "--cenario", Caldeira, "--saida", copy);

        output.WriteLine(stdout);
        Assert.Equal(SimRunApp.ExitOk, exit);
        Assert.Empty(stderr);
        Assert.Contains("Reproduzidos 500 passos (5 s)", stdout, StringComparison.Ordinal);
        Assert.Contains("idênticos", stdout, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(recording), File.ReadAllBytes(copy));
    }

    [Fact]
    public void AnEditedRecording_IsReportedAsDifferent()
    {
        string recording = Record(steps: 200);
        string[] lines = File.ReadAllLines(recording);
        string[] last = lines[^1].Split(',');
        last[^2] = "123.456"; // uma medição adulterada
        lines[^1] = string.Join(',', last);
        File.WriteAllText(recording, string.Join('\n', lines) + "\n");

        var (exit, stdout, _) = Invoke("reproduzir", recording, "--cenario", Caldeira, "--saida", Path.Combine(_folder, "copia.csv"));

        Assert.Equal(SimRunApp.ExitFailed, exit);
        Assert.Contains("diferentes", stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reproduzir")]
    [InlineData("reproduzir", "g.csv")]
    [InlineData("reproduzir", "g.csv", "--cenario", "c.json")]
    [InlineData("reproduzir", "g.csv", "--cenario", "c.json", "--saida", "s.csv", "--velocidade", "2")]
    public void BadArguments_ShowUsage(params string[] args)
    {
        var (exit, _, stderr) = Invoke(args);

        Assert.Equal(SimRunApp.ExitUsage, exit);
        Assert.Contains("simrun reproduzir", stderr, StringComparison.Ordinal);
    }

    private string Record(int steps)
    {
        var io = new PlantIo(ScenarioPlant.FromJson(File.ReadAllText(Caldeira)));
        string path = Path.Combine(_folder, "gravacao.csv");
        using var writer = new StreamWriter(path) { NewLine = "\n" };
        var recorder = new PlantRecorder(io, new RecordingHeader("caldeira", 101, 0.01, 10), writer.WriteLine);
        for (int n = 0; n < steps; n++)
        {
            if (n == 100)
            {
                io.Image.TryWriteHoldingRegister(0, AnalogCodec.ToRegister(20000));
            }

            io.Step();
            recorder.OnStepCompleted();
        }

        return path;
    }

    private static (int Exit, string Stdout, string Stderr) Invoke(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exit = SimRunApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
