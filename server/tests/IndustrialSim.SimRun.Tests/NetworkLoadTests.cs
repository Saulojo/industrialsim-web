using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IndustrialSim.Contracts;
using IndustrialSim.SimRun.NetworkLoad;

namespace IndustrialSim.SimRun.Tests;

/// <summary>
/// Partes do <c>simrun carga-rede</c> (Etapa 9A) que não precisam de servidor: argumentos, amostras de CPU e RAM da
/// VM, metas de cada patamar e conferência do certificado pela raiz do Caddy. O teste com o servidor de verdade
/// fica em <c>IndustrialSim.Server.Tests</c>.
/// </summary>
public class NetworkLoadTests
{
    // Duas amostras 5 s distantes numa VM de 4 núcleos (clk_tck 100): 2000 jiffies no total, 1500 ociosos
    // (25 % de CPU); o serviço gastou 100 jiffies = 1 s de CPU em 5 s (20 % de um núcleo).
    private const string First = "S 1000.00 4 100 500000 400000 4026531 3000000 2000 51200";
    private const string Second = "S 1005.00 4 100 502000 401500 4026531 2900000 2100 52224";

    [Fact]
    public void Sample_ParsesTheScriptLine()
    {
        Assert.True(ResourceSample.TryParse(First, out ResourceSample? sample));
        Assert.Equal(new ResourceSample(1000.0, 4, 100, 500000, 400000, 4026531, 3000000, 2000, 51200), sample);

        // Serviço parado: -1 nos campos do processo.
        Assert.True(ResourceSample.TryParse("S 12.5 2 100 10 5 1000 900 -1 -1", out ResourceSample? stopped));
        Assert.Equal(-1, stopped.ServiceCpu);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Warning: Permanently added '10.0.67.80' (ED25519) to the list of known hosts.")]
    [InlineData("S 1000 4 100 500000 400000 4026531 3000000 2000")]
    [InlineData("S 1000 0 100 500000 400000 4026531 3000000 2000 51200")]
    [InlineData("S 1000 4 100 x 400000 4026531 3000000 2000 51200")]
    public void Sample_IgnoresOtherLines(string? line) =>
        Assert.False(ResourceSample.TryParse(line, out _));

    [Fact]
    public void Stats_ComputeVmAndServiceUsageBetweenSamples()
    {
        var stats = new ResourceStats();
        Assert.Null(stats.Summarize());

        stats.Add(Parse(First));
        stats.Add(Parse(Second));
        ResourceSummary summary = stats.Summarize()!;

        Assert.Equal(2, summary.Samples);
        Assert.Equal(4, summary.VmCpus);
        Assert.Equal(0.25, summary.VmCpuMean, 6);
        Assert.Equal(0.25, summary.VmCpuMax, 6);
        Assert.Equal(0.20, summary.ServiceCoresMean, 6);
        Assert.Equal(4026531 / 1024.0, summary.MemTotalMb, 6);
        Assert.Equal((4026531 - 2900000) / 1024.0, summary.MemUsedMaxMb, 6);
        Assert.Equal(2900000.0 / 4026531, summary.MemAvailableMinFraction, 6);
        Assert.Equal(52224 / 1024.0, summary.ServiceRssMaxMb, 6);
    }

    [Fact]
    public void Stats_SkipTheServiceIntervalWhenItRestarted()
    {
        var stats = new ResourceStats();
        stats.Add(Parse(First));

        // Contador do serviço menor que o anterior: outro processo; a CPU da VM continua valendo.
        stats.Add(Parse("S 1005.00 4 100 502000 401500 4026531 2900000 30 20000"));
        ResourceSummary summary = stats.Summarize()!;

        Assert.Equal(0.0, summary.ServiceCoresMean);
        Assert.Equal(0.25, summary.VmCpuMean, 6);
    }

    [Fact]
    public void SamplerScript_IsEmbedded()
    {
        string script = RemoteHost.ReadSamplerScript();

        Assert.StartsWith("#!/bin/sh", script, StringComparison.Ordinal);
        Assert.Contains("/proc/stat", script, StringComparison.Ordinal);
        Assert.Contains("systemctl show -p MainPID", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("30", new[] { 30 })]
    [InlineData("30,60,100,200", new[] { 30, 60, 100, 200 })]
    [InlineData(" 30 , 60 ", new[] { 30, 60 })]
    public void Steps_AcceptOneNumberOrAnIncreasingList(string text, int[] expected)
    {
        Assert.True(NetworkLoadCommand.TryParseSteps(text, out int[]? steps));
        Assert.Equal(expected, steps);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("60,30")]
    [InlineData("30,30")]
    [InlineData("30,,60")]
    [InlineData("trinta")]
    public void Steps_RejectInvalidLists(string text) =>
        Assert.False(NetworkLoadCommand.TryParseSteps(text, out _));

    [Fact]
    public void Arguments_DefaultsAndErrors()
    {
        Assert.True(NetworkLoadCommand.TryParse(["--servidor", "https://10.0.67.80"], out var settings, out _));
        Assert.Equal(new Uri("https://10.0.67.80"), settings.Server);
        Assert.Equal([30], settings.PlantSteps);
        Assert.Equal(30.0, settings.MinutesPerStep);
        Assert.Equal("caldeira", settings.Scenario);
        Assert.Equal("industrialsim", settings.ServiceName);
        Assert.Null(settings.SshTarget);
        Assert.Null(settings.MasterAddress);

        Assert.False(NetworkLoadCommand.TryParse([], out _, out string? missing));
        Assert.Contains("--servidor", missing, StringComparison.Ordinal);
        Assert.False(NetworkLoadCommand.TryParse(["--servidor", "ftp://x"], out _, out _));
        Assert.False(NetworkLoadCommand.TryParse(["--servidor", "https://x", "--minutos", "0"], out _, out _));
        Assert.False(NetworkLoadCommand.TryParse(["--servidor", "https://x", "--ip-mestre", "abc"], out _, out _));
        Assert.False(NetworkLoadCommand.TryParse(["--servidor", "https://x", "--ca", "nao-existe.crt"], out _, out string? ca));
        Assert.Contains("--ca", ca, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithoutServer_PrintsUsage()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Assert.Equal(SimRunApp.ExitUsage, SimRunApp.Run(["carga-rede"], stdout, stderr));
        Assert.Contains("simrun carga-rede", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Goals_APhaseWithinAllGoalsPasses() =>
        Assert.Empty(NetworkLoadRunner.Evaluate(Phase(), new LoadGoals(), expectResources: true));

    [Fact]
    public void Goals_EachBrokenGoalIsListed()
    {
        PhaseReport bad = Phase() with
        {
            MaxDrift = 0.002,
            Loop = Loop(p99Ms: 7.5),
            ModbusErrors = 3,
            Resources = Resources(vmCpuMean: 0.9, memAvailableMin: 0.05),
        };

        IReadOnlyList<string> failures = NetworkLoadRunner.Evaluate(bad, new LoadGoals(), expectResources: true);

        Assert.Equal(5, failures.Count);
        Assert.Contains(failures, f => f.StartsWith("deriva", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.StartsWith("atraso do passo p99 7.50 ms", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.StartsWith("erros: 3 Modbus", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.StartsWith("CPU da VM 90.0 %", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.StartsWith("RAM disponível 5.0 %", StringComparison.Ordinal));
    }

    [Fact]
    public void Goals_MissingSamplesFailOnlyWhenSshWasGiven()
    {
        PhaseReport noSamples = Phase() with { Resources = null };

        Assert.Empty(NetworkLoadRunner.Evaluate(noSamples, new LoadGoals(), expectResources: false));
        Assert.Single(NetworkLoadRunner.Evaluate(noSamples, new LoadGoals(), expectResources: true));
    }

    [Fact]
    public void Goals_AnInterruptedPhaseFails()
    {
        PhaseReport interrupted = Phase() with { CreateError = "criação da planta 41: instance-limit-reached" };

        Assert.Equal(
            "patamar interrompido: criação da planta 41: instance-limit-reached",
            Assert.Single(NetworkLoadRunner.Evaluate(interrupted, new LoadGoals(), expectResources: true)));
    }

    [Fact]
    public void Durations_Percentiles()
    {
        DurationsMs stats = DurationsMs.From([.. Enumerable.Range(1, 100).Select(i => (double)i)]);

        Assert.Equal(new DurationsMs(100, 50, 99, 100), stats);
        Assert.Equal(new DurationsMs(0, 0, 0, 0), DurationsMs.From([]));
    }

    /// <summary>Certificado do servidor emitido por uma raiz própria (como a do <c>tls internal</c> do Caddy).</summary>
    [Fact]
    public void Certificate_IsAcceptedOnlyWithItsOwnRoot()
    {
        using X509Certificate2 root = CreateRoot("CN=Raiz de teste");
        using X509Certificate2 otherRoot = CreateRoot("CN=Outra raiz");
        using X509Certificate2 leaf = CreateLeaf(root, IPAddress.Parse("10.0.67.80"));
        using var chain = new X509Chain();
        chain.ChainPolicy.ExtraStore.Add(root);
        chain.Build(leaf);

        Assert.True(LoadServerClient.ValidateWithRoot(root, leaf, chain, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(LoadServerClient.ValidateWithRoot(otherRoot, leaf, chain, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(LoadServerClient.ValidateWithRoot(root, leaf, chain, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(LoadServerClient.ValidateWithRoot(root, null, chain, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    private static ResourceSample Parse(string line)
    {
        Assert.True(ResourceSample.TryParse(line, out ResourceSample? sample));
        return sample;
    }

    private static PhaseReport Phase() => new(
        Plants: 30,
        PlantsCreated: 30,
        CreateError: null,
        WallSeconds: 1800,
        ServiceRestarted: true,
        Loop: Loop(p99Ms: 0.8),
        MaxDrift: 0.0001,
        PlantFaults: 0,
        ModbusScans: 540000,
        ModbusErrors: 0,
        ModbusReconnects: 0,
        LastModbusError: null,
        ModbusScanMs: new DurationsMs(540000, 2, 5, 20),
        HttpErrors: 0,
        Resources: Resources(vmCpuMean: 0.1, memAvailableMin: 0.6),
        ToolCores: 0.05,
        Failures: []);

    private static LoopMetricsResponse Loop(double p99Ms) => new(
        2, 30, "linux-sleep", 180000, 5400000, 0, 0, 0,
        new DurationStats(5400000, 0.3, 0.3, p99Ms, 3.0),
        new DurationStats(5400000, 0.004, 0.003, 0.02, 1.0));

    private static ResourceSummary Resources(double vmCpuMean, double memAvailableMin) =>
        new(360, 4, vmCpuMean, vmCpuMean, 0.05, 0.1, 3900, 1000, memAvailableMin, 120);

    private static X509Certificate2 CreateRoot(string subject)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static X509Certificate2 CreateLeaf(X509Certificate2 issuer, IPAddress address)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=servidor", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(address);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        byte[] serial = [1, 2, 3, 4];
        using X509Certificate2 signed = request.Create(issuer, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), serial);
        return signed.CopyWithPrivateKey(key);
    }
}
