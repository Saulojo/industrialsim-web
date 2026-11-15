namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Seção <c>io</c> do cenário: o mapa Modbus da planta, como dados. Liga endereços das quatro áreas às entradas
/// e saídas externas do cenário. A imagem de I/O e as conversões ficam em <c>IndustrialSim.IO</c>.
/// </summary>
/// <param name="Coils">Saídas digitais do mestre, ligadas a entradas externas (0 ou 1).</param>
/// <param name="DiscreteInputs">Entradas digitais do mestre, ligadas a saídas externas (≥ 0,5 = 1).</param>
/// <param name="HoldingRegisters">Saídas analógicas do mestre (comandos 4–20 mA), ligadas a entradas externas.</param>
/// <param name="InputRegisters">Entradas analógicas do mestre (medições 4–20 mA), ligadas a saídas externas.</param>
/// <param name="Diagnostics">Bloco de diagnóstico em unidades de engenharia, nos input registers.</param>
/// <param name="Watchdog">Heartbeat e estado seguro.</param>
public sealed record IoDefinition(
    IReadOnlyList<DigitalPointDefinition> Coils,
    IReadOnlyList<DigitalPointDefinition> DiscreteInputs,
    IReadOnlyList<AnalogPointDefinition> HoldingRegisters,
    IReadOnlyList<AnalogPointDefinition> InputRegisters,
    DiagnosticsDefinition? Diagnostics,
    WatchdogDefinition? Watchdog)
{
    /// <summary>
    /// Registradores que os sinais analógicos ocupam no formato <c>float32</c> (D27): o sinal de endereço
    /// <c>a</c> ocupa <c>2a</c> e <c>2a+1</c>. Zero se não houver sinais.
    /// </summary>
    public static int AnalogBlockLength(IReadOnlyList<AnalogPointDefinition> points) =>
        points.Count == 0 ? 0 : (2 * points.Max(p => p.Address)) + 2;
}

/// <summary>Ponto digital: endereço (base 0) e o nome da entrada ou saída externa.</summary>
public sealed record DigitalPointDefinition(int Address, string Signal);

/// <summary>
/// Ponto analógico. <paramref name="Lrv"/> e <paramref name="Urv"/> são os valores do sinal, na unidade da
/// planta, em 4 e 20 mA. <paramref name="Unit"/> e <paramref name="UnitFactor"/> dão a unidade de engenharia
/// usada no formato <c>float32</c> e na interface: valor de engenharia = valor do sinal × fator (D29).
/// </summary>
public sealed record AnalogPointDefinition(int Address, string Signal, double Lrv, double Urv, string Unit, double UnitFactor);

/// <summary>Bloco de diagnóstico: a partir de <paramref name="Address"/>, um <c>float32</c> (2 registradores) por valor.</summary>
public sealed record DiagnosticsDefinition(int Address, IReadOnlyList<DiagnosticValueDefinition> Values);

/// <summary>Valor de diagnóstico: saída externa, unidade e fator (valor de engenharia = valor do sinal × fator).</summary>
public sealed record DiagnosticValueDefinition(string Signal, string Unit, double UnitFactor);

/// <summary>
/// Watchdog: se o holding register <paramref name="HeartbeatRegister"/> ficar <paramref name="TimeoutSeconds"/>
/// sem mudar, as entradas externas de <paramref name="SafeState"/> recebem os valores seguros, e a entrada
/// discreta <paramref name="StatusDiscreteInput"/> (se houver) vai a 1.
/// </summary>
public sealed record WatchdogDefinition(
    int HeartbeatRegister,
    int? StatusDiscreteInput,
    double TimeoutSeconds,
    IReadOnlyList<SafeStateDefinition> SafeState);

/// <summary>Valor que uma entrada externa recebe no estado seguro, na unidade da planta.</summary>
public sealed record SafeStateDefinition(string Input, double Value);
