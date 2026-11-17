namespace IndustrialSim.Sessions.Tests;

/// <summary>RNF08: as plantas em execução não dependem do host web; o host é que depende delas.</summary>
public class ArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "IndustrialSim.Server",
    ];

    [Fact]
    public void Sessions_DoesNotReferenceTheWebHost()
    {
        var referenced = SessionsAssembly.Reference.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        var forbidden = referenced.Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)));

        Assert.Empty(forbidden);
    }
}
