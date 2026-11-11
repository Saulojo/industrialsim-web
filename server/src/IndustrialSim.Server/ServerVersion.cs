using System.Reflection;

namespace IndustrialSim.Server;

/// <summary>
/// Versão do servidor lida do assembly (Version do Directory.Build.props + commit do git).
/// </summary>
public static class ServerVersion
{
    public static string Current { get; } =
        typeof(ServerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? "0.0.0";
}
