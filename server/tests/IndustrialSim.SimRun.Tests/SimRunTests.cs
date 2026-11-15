using System.Globalization;
using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.SimRun.Tests;

public sealed class SimRunTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("simrun-tests-").FullName;

    private static string WaterTank => Path.Combine(AppContext.BaseDirectory, "TestScenarios", "water-tank.json");

    private static string Caldeira => Path.Combine(AppContext.BaseDirectory, "ProductScenarios", "caldeira", "scenario.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>
    /// <c>simrun cenario.json --entradas roteiro.csv --saida resultado.csv</c>: o CSV de saída tem o tempo, as
    /// entradas e as saídas, e os valores são os mesmos de rodar a planta diretamente com os mesmos comandos.
    /// </summary>
    [Fact]
    public void Run_WritesResultMatchingDirectSimulation()
    {
        string script = Write("roteiro.csv", """
            time,pumpRun,valveCommand,outletOpening
            0,1,0.5,1
            # a 20 s a válvula abre mais e a carga cai
            20,,0.9,0.6
            40,0,,
            60,,,
            """);
        string result = Path.Combine(_folder, "resultado.csv");

        var (exit, stdout, stderr) = Invoke(WaterTank, "--entradas", script, "--saida", result);

        output.WriteLine(stdout);
        Assert.Equal(SimRunApp.ExitOk, exit);
        Assert.Empty(stderr);

        string[] lines = File.ReadAllLines(result);
        Assert.Equal("time,pumpRun,valveCommand,outletOpening,level,measuredLevel", lines[0]);
        Assert.Equal(6002, lines.Length); // cabeçalho + instante inicial + 6000 passos
        Assert.Equal("0,1,0.5,1,0.5,0.5", lines[1]);

        // A mesma planta, rodada direto.
        var plant = ScenarioPlant.FromJson(File.ReadAllText(WaterTank));
        plant.SetInput("pumpRun", 1.0);
        plant.SetInput("valveCommand", 0.5);
        for (int n = 0; n < 6000; n++)
        {
            if (n == 2000)
            {
                plant.SetInput("valveCommand", 0.9);
                plant.SetInput("outletOpening", 0.6);
            }

            if (n == 4000)
            {
                plant.SetInput("pumpRun", 0.0);
            }

            plant.Step();

            if (n is 1999 or 2000 or 3999 or 4000 or 5999)
            {
                double[] cells = [.. lines[n + 2].Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture))];
                Assert.Equal(plant.Clock.TimeSeconds, cells[0]);
                Assert.Equal(plant.GetInput("pumpRun"), cells[1]);
                Assert.Equal(plant.GetInput("valveCommand"), cells[2]);
                Assert.Equal(plant.GetInput("outletOpening"), cells[3]);
                Assert.Equal(plant.GetOutput("level"), cells[4]); // formato "R": o valor relido é idêntico
                Assert.Equal(plant.GetOutput("measuredLevel"), cells[5]);
            }
        }

        Assert.Contains("6000 passos", stdout, StringComparison.Ordinal);
    }

    /// <summary>Duas execuções iguais geram arquivos idênticos byte a byte e o mesmo hash (RNF02).</summary>
    [Fact]
    public void Run_IsDeterministic()
    {
        string script = Write("roteiro.csv", "time,FV-102_command,FV-103_command\n0,0.5,0.5\n30,0.7,\n90,,0.3\n");
        string first = Path.Combine(_folder, "a.csv");
        string second = Path.Combine(_folder, "b.csv");
        string otherSeed = Path.Combine(_folder, "c.csv");

        var runA = Invoke(Caldeira, "--entradas", script, "--saida", first, "--duracao", "180", "--intervalo", "1");
        var runB = Invoke(Caldeira, "--entradas", script, "--saida", second, "--duracao", "180", "--intervalo", "1");
        var runC = Invoke(Caldeira, "--entradas", script, "--saida", otherSeed, "--duracao", "180", "--intervalo", "1", "--semente", "5");

        output.WriteLine(runA.Stdout);
        Assert.Equal(SimRunApp.ExitOk, runA.Exit);
        Assert.Equal(runA.Stdout.Replace(first, string.Empty, StringComparison.Ordinal), runB.Stdout.Replace(second, string.Empty, StringComparison.Ordinal));
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.NotEqual(File.ReadAllText(first), File.ReadAllText(otherSeed));
        Assert.Equal(182, File.ReadAllLines(first).Length); // cabeçalho + t = 0 + 180 linhas (uma por segundo)
    }

    [Fact]
    public void ScenarioOnly_ValidatesAndDescribes()
    {
        var (exit, stdout, _) = Invoke(Caldeira);

        output.WriteLine(stdout);
        Assert.Equal(SimRunApp.ExitOk, exit);
        Assert.Contains("Cenário 'caldeira': 15 componentes", stdout, StringComparison.Ordinal);
        Assert.Contains("FV-101_command", stdout, StringComparison.Ordinal);
        Assert.Contains("LT-101", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidScenario_ListsErrorsWithPathsAndFails()
    {
        string scenario = Write("ruim.json", File.ReadAllText(WaterTank).Replace("\"areaM2\": 1.0", "\"areaM2\": -1.0", StringComparison.Ordinal));

        var (exit, _, stderr) = Invoke(scenario, "--saida", Path.Combine(_folder, "nada.csv"));

        output.WriteLine(stderr);
        Assert.Equal(SimRunApp.ExitFailed, exit);
        Assert.Contains("components[2].params.areaM2", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_folder, "nada.csv")));
    }

    [Fact]
    public void ScriptWithUnknownInput_Fails()
    {
        string script = Write("roteiro.csv", "time,bomba\n0,1\n10,0\n");

        var (exit, _, stderr) = Invoke(WaterTank, "--entradas", script, "--saida", Path.Combine(_folder, "r.csv"));

        output.WriteLine(stderr);
        Assert.Equal(SimRunApp.ExitFailed, exit);
        Assert.Contains("'bomba'", stderr, StringComparison.Ordinal);
        Assert.Contains("pumpRun", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFile_Fails()
    {
        var (exit, _, stderr) = Invoke(Path.Combine(_folder, "nao-existe.json"));

        Assert.Equal(SimRunApp.ExitFailed, exit);
        Assert.Contains("Erro de arquivo", stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("a.json", "b.json")]
    [InlineData("a.json", "--saida")]
    [InlineData("a.json", "--duracao", "-5")]
    [InlineData("a.json", "--velocidade", "2")]
    public void BadArguments_ShowUsage(params string[] args)
    {
        var (exit, _, stderr) = Invoke(args);

        Assert.Equal(SimRunApp.ExitUsage, exit);
        Assert.Contains("Uso:", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void NoDuration_IsUsageError()
    {
        var (exit, _, stderr) = Invoke(WaterTank, "--saida", Path.Combine(_folder, "r.csv"));

        Assert.Equal(SimRunApp.ExitUsage, exit);
        Assert.Contains("--duracao", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_IsWrittenToFile()
    {
        string path = Path.Combine(_folder, "scenario.schema.json");

        var (exit, _, _) = Invoke("--esquema", path);

        Assert.Equal(SimRunApp.ExitOk, exit);
        Assert.Equal(ScenarioSchemaWriter.Write(), File.ReadAllText(path));
    }

    private (int Exit, string Stdout, string Stderr) Invoke(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exit = SimRunApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }
}
