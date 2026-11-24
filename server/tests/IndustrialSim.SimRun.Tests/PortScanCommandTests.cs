using System.Net;
using System.Net.Sockets;

namespace IndustrialSim.SimRun.Tests;

/// <summary><c>simrun portas</c> (Etapa 9A): varredura em loopback, com portas abertas de verdade.</summary>
public class PortScanCommandTests
{
    [Fact]
    public async Task Scan_FindsListeningPortsAndRefusals()
    {
        using var first = new TcpListener(IPAddress.Loopback, 0);
        using var second = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        second.Start();
        int a = ((IPEndPoint)first.LocalEndpoint).Port;
        int b = ((IPEndPoint)second.LocalEndpoint).Port;
        int low = Math.Min(a, b);
        int high = Math.Max(a, b);

        // Uma faixa pequena em volta das duas portas; as outras, sem programa, recusam na hora no loopback.
        int from = Math.Max(1, low - 3);
        int to = Math.Min(65535, high + 3);
        PortScanCommand.ScanResult result = await PortScanCommand.ScanAsync(IPAddress.Loopback, from, to, 64, TimeSpan.FromSeconds(2));

        Assert.Contains(a, result.Open);
        Assert.Contains(b, result.Open);
        Assert.Equal(to - from + 1, result.Open.Count + result.Refused + result.TimedOut);
        Assert.Equal(result.Open.OrderBy(p => p), result.Open);
    }

    [Fact]
    public void Run_FailsWhenAnUnexpectedPortIsOpen()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string port = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int unexpected = SimRunApp.Run(["portas", "--host", "127.0.0.1", "--faixa", $"{port}-{port}", "--esperadas", "443"], stdout, stderr);
        Assert.Equal(SimRunApp.ExitFailed, unexpected);
        Assert.Contains($"[FALHOU] portas abertas fora das esperadas: {port}", stdout.ToString(), StringComparison.Ordinal);

        using var stdout2 = new StringWriter();
        int expected = SimRunApp.Run(["portas", "--host", "127.0.0.1", "--faixa", $"{port}-{port}", "--esperadas", $"443,{port}"], stdout2, stderr);
        Assert.Equal(SimRunApp.ExitOk, expected);
        Assert.Contains("[OK]", stdout2.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }

    [Theory]
    [InlineData("22,443,15000-15002", new[] { 22, 443, 15000, 15001, 15002 })]
    [InlineData(" 443 ", new[] { 443 })]
    [InlineData("", new int[0])]
    public void Ports_ParseListsAndRanges(string text, int[] expected)
    {
        Assert.True(PortScanCommand.TryParsePorts(text, out HashSet<int>? ports));
        Assert.Equal(expected, ports.Order());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("100-50")]
    [InlineData("1-2-3")]
    [InlineData("abc")]
    public void Ports_RejectInvalidText(string text) =>
        Assert.False(PortScanCommand.TryParsePorts(text, out _));

    [Fact]
    public void Ports_AreDescribedAsRanges()
    {
        Assert.Equal("22, 443, 15000-15999", PortScanCommand.Describe([.. Enumerable.Range(15000, 1000), 443, 22]));
        Assert.Equal("nenhuma", PortScanCommand.Describe([]));
        Assert.Equal("1-3, 5", PortScanCommand.Describe([3, 1, 2, 5, 2]));
    }

    [Fact]
    public void Arguments_RequireAHostAndOneContinuousRange()
    {
        Assert.True(PortScanCommand.TryParse(["--host", "10.0.67.80"], out var settings, out _));
        Assert.Equal(1, settings.First);
        Assert.Equal(65535, settings.Last);
        Assert.Contains(443, settings.Expected);
        Assert.Contains(15999, settings.Expected);

        Assert.False(PortScanCommand.TryParse([], out _, out _));
        Assert.False(PortScanCommand.TryParse(["--host", "vm.local"], out _, out _));
        Assert.False(PortScanCommand.TryParse(["--host", "10.0.67.80", "--faixa", "1-10,20-30"], out _, out _));
        Assert.False(PortScanCommand.TryParse(["--host", "10.0.67.80", "--paralelas", "0"], out _, out _));
    }

    [Fact]
    public async Task Probe_OfAClosedLoopbackPort_IsARefusal()
    {
        // Uma porta que acabou de ser liberada: no loopback, sem firewall, a conexão é recusada na hora.
        int port;
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        PortScanCommand.ScanResult result = await PortScanCommand.ScanAsync(IPAddress.Loopback, port, port, 1, TimeSpan.FromSeconds(2));
        Assert.Empty(result.Open);
        Assert.Equal(1, result.Refused + result.TimedOut);
    }
}
