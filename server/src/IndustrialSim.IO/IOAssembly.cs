using System.Reflection;

namespace IndustrialSim.IO;

/// <summary>
/// Referência ao assembly da imagem de I/O. A imagem de I/O não depende de rede (RNF08).
/// </summary>
public static class IOAssembly
{
    public static Assembly Reference => typeof(IOAssembly).Assembly;
}
