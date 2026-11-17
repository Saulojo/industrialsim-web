using System.Text.Json;

namespace IndustrialSim.SimRun.Tests;

/// <summary>
/// <c>simrun carga</c> em versão curta. Não confere as metas (o CI roda em máquinas compartilhadas): o ensaio
/// de 10 minutos é feito à parte e registrado nas evidências.
/// </summary>
public sealed class LoadCommandTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("simrun-load-").FullName;

    private static string Caldeira => Path.Combine(AppContext.BaseDirectory, "ProductScenarios", "caldeira", "scenario.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AShortRun_ReportsDriftLatenessAndCpu()
    {
        string report = Path.Combine(_folder, "relatorio.json");

        var (exit, stdout, stderr) = Invoke("carga", "--cenario", Caldeira, "--plantas", "3", "--minutos", "0.05", "--relatorio", report);

        output.WriteLine(stdout);
        Assert.Empty(stderr);
        Assert.Contains(exit, new[] { SimRunApp.ExitOk, SimRunApp.ExitFailed });
        Assert.Contains("deriva máxima", stdout, StringComparison.Ordinal);
        Assert.Contains("atraso do passo p99", stdout, StringComparison.Ordinal);
        Assert.Contains("CPU do processo", stdout, StringComparison.Ordinal);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(report));
        JsonElement root = json.RootElement;
        Assert.Equal(3, root.GetProperty("Plants").GetInt32());
        Assert.True(root.GetProperty("Steps").GetInt64() > 3 * 100, "Menos de 1 s simulado por planta em 3 s.");
        Assert.True(root.GetProperty("MasterScans").GetInt64() > 10);
        Assert.Equal(exit == SimRunApp.ExitOk, root.GetProperty("DriftMet").GetBoolean() && root.GetProperty("LatenessMet").GetBoolean() && root.GetProperty("CpuMet").GetBoolean());
    }

    [Theory]
    [InlineData("carga")]
    [InlineData("carga", "--cenario", "c.json", "--plantas", "0")]
    [InlineData("carga", "--cenario", "c.json", "--minutos", "-1")]
    [InlineData("carga", "--cenario", "c.json", "extra")]
    public void BadArguments_ShowUsage(params string[] args)
    {
        var (exit, _, stderr) = Invoke(args);

        Assert.Equal(SimRunApp.ExitUsage, exit);
        Assert.Contains("simrun carga", stderr, StringComparison.Ordinal);
    }

    private static (int Exit, string Stdout, string Stderr) Invoke(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exit = SimRunApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
