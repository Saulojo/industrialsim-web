using System.Reflection;

namespace IndustrialSim.Sessions;

/// <summary>
/// Referência ao assembly das plantas em execução. Não depende do host web (RNF08).
/// </summary>
public static class SessionsAssembly
{
    public static Assembly Reference => typeof(SessionsAssembly).Assembly;
}
