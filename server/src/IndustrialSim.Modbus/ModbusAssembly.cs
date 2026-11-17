using System.Reflection;

namespace IndustrialSim.Modbus;

/// <summary>
/// Referência ao assembly do servidor Modbus. Não depende do host web nem das plantas em execução (RNF08).
/// </summary>
public static class ModbusAssembly
{
    public static Assembly Reference => typeof(ModbusAssembly).Assembly;
}
