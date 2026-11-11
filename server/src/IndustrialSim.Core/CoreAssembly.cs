using System.Reflection;

namespace IndustrialSim.Core;

/// <summary>
/// Referência ao assembly do núcleo. O núcleo não faz I/O, não lê relógio e não usa rede (RNF08).
/// </summary>
public static class CoreAssembly
{
    public static Assembly Reference => typeof(CoreAssembly).Assembly;
}
