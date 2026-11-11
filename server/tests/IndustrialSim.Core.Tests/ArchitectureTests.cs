using IndustrialSim.Core;

namespace IndustrialSim.Core.Tests;

/// <summary>
/// RNF08: o núcleo não depende de rede nem de interface.
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
        "IndustrialSim.Sessions",
    ];

    [Fact]
    public void Core_DoesNotReferenceNetworkOrHostAssemblies()
    {
        var referenced = CoreAssembly.Reference.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        var forbidden = referenced.Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)));

        Assert.Empty(forbidden);
    }
}
