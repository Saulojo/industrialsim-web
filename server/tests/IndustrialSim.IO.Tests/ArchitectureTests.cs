using IndustrialSim.IO;

namespace IndustrialSim.IO.Tests;

/// <summary>
/// RNF08: a imagem de I/O não depende de rede nem do host; Modbus e HART é que dependem dela.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "System.Net",
        "Microsoft.AspNetCore",
        "IndustrialSim.Server",
        "IndustrialSim.Modbus",
        "IndustrialSim.Hart",
    ];

    [Fact]
    public void IO_DoesNotReferenceNetworkOrHostAssemblies()
    {
        var referenced = IOAssembly.Reference.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        var forbidden = referenced.Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)));

        Assert.Empty(forbidden);
    }
}
