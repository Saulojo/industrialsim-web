using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.IO;

/// <summary>Ponto digital do mapa, já ligado ao índice da entrada ou saída externa da planta.</summary>
public sealed record DigitalPoint(int Address, string Signal, int PlantIndex);

/// <summary>Ponto analógico do mapa, já ligado ao índice da entrada ou saída externa da planta.</summary>
public sealed record AnalogPoint(int Address, string Signal, int PlantIndex, double Lrv, double Urv, string Unit, double UnitFactor);

/// <summary>Valor do bloco de diagnóstico, ligado ao índice da saída externa da planta.</summary>
public sealed record DiagnosticPoint(string Signal, int PlantIndex, string Unit, double UnitFactor);

/// <summary>Entrada externa e o valor que ela recebe no estado seguro do watchdog.</summary>
public sealed record SafeStatePoint(string Input, int PlantIndex, double Value);

/// <summary>Uma linha do mapa de I/O, para exibir ou exportar: endereço nas duas notações, sinal e unidade.</summary>
/// <param name="Area">Área Modbus.</param>
/// <param name="Address">Endereço em base 0 do primeiro registrador.</param>
/// <param name="ClassicAddress">O mesmo endereço na notação clássica.</param>
/// <param name="Length">Quantos endereços o sinal ocupa (2 para <c>float32</c>).</param>
/// <param name="Signal">Nome da entrada ou saída externa; <c>heartbeat</c> e <c>watchdog</c> para os sinais do watchdog.</param>
/// <param name="Unit">Unidade de engenharia; vazio se não se aplica.</param>
public sealed record IoMapEntry(ModbusArea Area, int Address, int ClassicAddress, int Length, string Signal, string Unit);

/// <summary>
/// Mapa de I/O de uma planta: a seção <c>io</c> do cenário resolvida contra as entradas e saídas externas, com o
/// tamanho de cada área. A imagem de I/O é dimensionada por este mapa, e não pelos 65.536 endereços de cada área.
/// </summary>
public sealed class IoMap
{
    /// <summary>Nome do heartbeat em <see cref="Describe"/>.</summary>
    public const string HeartbeatSignal = "heartbeat";

    /// <summary>Nome do estado do watchdog em <see cref="Describe"/>.</summary>
    public const string WatchdogSignal = "watchdog";

    private IoMap(
        DigitalPoint[] coils,
        DigitalPoint[] discreteInputs,
        AnalogPoint[] holdingRegisters,
        AnalogPoint[] inputRegisters,
        int? diagnosticsAddress,
        DiagnosticPoint[] diagnostics,
        WatchdogDefinition? watchdog,
        SafeStatePoint[] safeState)
    {
        Coils = coils;
        DiscreteInputs = discreteInputs;
        HoldingRegisters = holdingRegisters;
        InputRegisters = inputRegisters;
        DiagnosticsAddress = diagnosticsAddress;
        Diagnostics = diagnostics;
        HeartbeatRegister = watchdog?.HeartbeatRegister;
        WatchdogStatusDiscreteInput = watchdog?.StatusDiscreteInput;
        DefaultWatchdogTimeoutSeconds = watchdog?.TimeoutSeconds ?? ScenarioLoader.DefaultWatchdogTimeoutSeconds;
        SafeState = safeState;

        HoldingAnalogLength = AnalogLength(holdingRegisters);
        InputAnalogLength = AnalogLength(inputRegisters);

        CoilCount = coils.Length == 0 ? 0 : coils.Max(p => p.Address) + 1;
        DiscreteInputCount = Math.Max(
            discreteInputs.Length == 0 ? 0 : discreteInputs.Max(p => p.Address) + 1,
            WatchdogStatusDiscreteInput is int status ? status + 1 : 0);
        HoldingRegisterCount = Math.Max(HoldingAnalogLength, HeartbeatRegister is int heartbeat ? heartbeat + 1 : 0);
        InputRegisterCount = Math.Max(InputAnalogLength, diagnosticsAddress is int address ? address + (2 * diagnostics.Length) : 0);
    }

    public IReadOnlyList<DigitalPoint> Coils { get; }

    public IReadOnlyList<DigitalPoint> DiscreteInputs { get; }

    public IReadOnlyList<AnalogPoint> HoldingRegisters { get; }

    public IReadOnlyList<AnalogPoint> InputRegisters { get; }

    /// <summary>Primeiro registrador do bloco de diagnóstico; nulo se o cenário não tiver o bloco.</summary>
    public int? DiagnosticsAddress { get; }

    public IReadOnlyList<DiagnosticPoint> Diagnostics { get; }

    /// <summary>Holding register do heartbeat; nulo se o cenário não tiver watchdog.</summary>
    public int? HeartbeatRegister { get; }

    /// <summary>Entrada discreta que indica o watchdog disparado; nula se não houver.</summary>
    public int? WatchdogStatusDiscreteInput { get; }

    /// <summary>Tempo limite do watchdog definido no cenário, em segundos.</summary>
    public double DefaultWatchdogTimeoutSeconds { get; }

    public IReadOnlyList<SafeStatePoint> SafeState { get; }

    /// <summary>Tamanho de cada área: do endereço 0 ao maior endereço usado, em qualquer formato.</summary>
    public int CoilCount { get; }

    public int DiscreteInputCount { get; }

    public int HoldingRegisterCount { get; }

    public int InputRegisterCount { get; }

    /// <summary>Registradores, a partir do 0, reservados aos comandos analógicos (o arranjo <c>float32</c> é o maior).</summary>
    public int HoldingAnalogLength { get; }

    /// <summary>Registradores, a partir do 0, reservados às medições analógicas.</summary>
    public int InputAnalogLength { get; }

    /// <summary>
    /// Registrador onde começa o sinal analógico de endereço <paramref name="pointAddress"/>: o próprio endereço
    /// nos formatos inteiros; <c>2a</c> no <c>float32</c>, em que cada sinal ocupa dois registradores (D27).
    /// </summary>
    public static int RegisterAddress(int pointAddress, AnalogFormat format) =>
        format == AnalogFormat.Float32 ? 2 * pointAddress : pointAddress;

    /// <summary>Monta o mapa da planta. Lança <see cref="InvalidOperationException"/> se o cenário não tiver a seção <c>io</c>.</summary>
    public static IoMap Create(ScenarioPlant plant)
    {
        ArgumentNullException.ThrowIfNull(plant);
        IoDefinition io = plant.Definition.Io
            ?? throw new InvalidOperationException($"O cenário '{plant.Definition.Id}' não tem a seção 'io' (mapa Modbus).");

        return new IoMap(
            [.. io.Coils.Select(p => new DigitalPoint(p.Address, p.Signal, plant.InputIndex(p.Signal)))],
            [.. io.DiscreteInputs.Select(p => new DigitalPoint(p.Address, p.Signal, plant.OutputIndex(p.Signal)))],
            [.. io.HoldingRegisters.Select(p => new AnalogPoint(p.Address, p.Signal, plant.InputIndex(p.Signal), p.Lrv, p.Urv, p.Unit, p.UnitFactor))],
            [.. io.InputRegisters.Select(p => new AnalogPoint(p.Address, p.Signal, plant.OutputIndex(p.Signal), p.Lrv, p.Urv, p.Unit, p.UnitFactor))],
            io.Diagnostics?.Address,
            [.. (io.Diagnostics?.Values ?? []).Select(v => new DiagnosticPoint(v.Signal, plant.OutputIndex(v.Signal), v.Unit, v.UnitFactor))],
            io.Watchdog,
            [.. (io.Watchdog?.SafeState ?? []).Select(s => new SafeStatePoint(s.Input, plant.InputIndex(s.Input), s.Value))]);
    }

    /// <summary>
    /// O mapa como lista de linhas, com endereços em base 0 e na notação clássica, no formato analógico dado
    /// (no <c>float32</c> os sinais analógicos mudam de endereço e ocupam 2 registradores).
    /// </summary>
    public IReadOnlyList<IoMapEntry> Describe(AnalogFormat format)
    {
        var entries = new List<IoMapEntry>();
        int analogLength = format == AnalogFormat.Float32 ? 2 : 1;

        foreach (DigitalPoint point in Coils)
        {
            entries.Add(Entry(ModbusArea.Coil, point.Address, 1, point.Signal, string.Empty));
        }

        foreach (DigitalPoint point in DiscreteInputs)
        {
            entries.Add(Entry(ModbusArea.DiscreteInput, point.Address, 1, point.Signal, string.Empty));
        }

        if (WatchdogStatusDiscreteInput is int status)
        {
            entries.Add(Entry(ModbusArea.DiscreteInput, status, 1, WatchdogSignal, string.Empty));
        }

        foreach (AnalogPoint point in HoldingRegisters)
        {
            entries.Add(Entry(ModbusArea.HoldingRegister, RegisterAddress(point.Address, format), analogLength, point.Signal, point.Unit));
        }

        if (HeartbeatRegister is int heartbeat)
        {
            entries.Add(Entry(ModbusArea.HoldingRegister, heartbeat, 1, HeartbeatSignal, string.Empty));
        }

        foreach (AnalogPoint point in InputRegisters)
        {
            entries.Add(Entry(ModbusArea.InputRegister, RegisterAddress(point.Address, format), analogLength, point.Signal, point.Unit));
        }

        if (DiagnosticsAddress is int diagnostics)
        {
            for (int k = 0; k < Diagnostics.Count; k++)
            {
                entries.Add(Entry(ModbusArea.InputRegister, diagnostics + (2 * k), 2, Diagnostics[k].Signal, Diagnostics[k].Unit));
            }
        }

        return entries;

        static IoMapEntry Entry(ModbusArea area, int address, int length, string signal, string unit) =>
            new(area, address, ModbusAddress.Classic(area, address), length, signal, unit);
    }

    private static int AnalogLength(AnalogPoint[] points) => points.Length == 0 ? 0 : (2 * points.Max(p => p.Address)) + 2;
}
