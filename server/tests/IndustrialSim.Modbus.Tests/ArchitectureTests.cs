namespace IndustrialSim.Modbus.Tests;

/// <summary>RNF08: o servidor Modbus não depende do host web nem das plantas em execução.</summary>
public class ArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "IndustrialSim.Server",
        "IndustrialSim.Sessions",
        "FluentModbus",
    ];

    [Fact]
    public void Modbus_DoesNotReferenceTheHostTheSessionsOrTheTestClientLibrary()
    {
        var referenced = ModbusAssembly.Reference.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        var forbidden = referenced.Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)));

        Assert.Empty(forbidden);
    }
}
