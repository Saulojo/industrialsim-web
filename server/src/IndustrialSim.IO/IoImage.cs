using System.Collections.Immutable;

namespace IndustrialSim.IO;

/// <summary>
/// Cópia instantânea e coerente da imagem de I/O: as quatro áreas, as marcas de "forçado" e o formato em que os
/// registradores analógicos estão, tudo do mesmo passo.
/// </summary>
/// <param name="Coils">Coils, do endereço 0 ao último usado.</param>
/// <param name="DiscreteInputs">Entradas discretas.</param>
/// <param name="HoldingRegisters">Holding registers.</param>
/// <param name="InputRegisters">Input registers.</param>
/// <param name="ForcedDiscreteInputs">Para cada entrada discreta, se o valor está forçado.</param>
/// <param name="ForcedInputRegisters">Para cada input register, se o valor está forçado.</param>
/// <param name="Format">Formato analógico em que os registradores foram publicados.</param>
/// <param name="WordOrder">Ordem das palavras dos <c>float32</c>.</param>
/// <param name="StepCount">Passo da planta a que as medições correspondem.</param>
/// <param name="WatchdogTripped">Se o watchdog estava disparado nesse passo.</param>
public sealed record IoSnapshot(
    ImmutableArray<bool> Coils,
    ImmutableArray<bool> DiscreteInputs,
    ImmutableArray<ushort> HoldingRegisters,
    ImmutableArray<ushort> InputRegisters,
    ImmutableArray<bool> ForcedDiscreteInputs,
    ImmutableArray<bool> ForcedInputRegisters,
    AnalogFormat Format,
    WordOrder WordOrder,
    long StepCount,
    bool WatchdogTripped);

/// <summary>
/// Imagem de I/O de uma planta: as quatro áreas do Modbus, dimensionadas pelo mapa do cenário. É o ponto de
/// encontro entre as threads da rede (mestres Modbus, a partir da Etapa 6) e a thread do passo da planta;
/// todo acesso passa por uma trava, e cada operação é atômica.
/// </summary>
/// <remarks>
/// O mestre escreve coils e holding registers e lê as quatro áreas; a planta lê os comandos no início do passo
/// e publica as medições no fim (<see cref="PlantIo"/>). Um endereço fora do mapa faz a operação inteira
/// falhar (retorna falso), o que vira a exceção "endereço ilegal" no servidor Modbus.
/// </remarks>
public sealed class IoImage
{
    private readonly Lock _gate = new();
    private readonly bool[] _coils;
    private readonly bool[] _discreteInputs;
    private readonly ushort[] _holdingRegisters;
    private readonly ushort[] _inputRegisters;
    private readonly bool[] _forcedDiscreteInputs;
    private readonly bool[] _forcedInputRegisters;
    private AnalogFormat _format;
    private WordOrder _wordOrder;
    private long _stepCount;
    private bool _watchdogTripped;

    public IoImage(int coilCount, int discreteInputCount, int holdingRegisterCount, int inputRegisterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coilCount);
        ArgumentOutOfRangeException.ThrowIfNegative(discreteInputCount);
        ArgumentOutOfRangeException.ThrowIfNegative(holdingRegisterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputRegisterCount);

        _coils = new bool[coilCount];
        _discreteInputs = new bool[discreteInputCount];
        _holdingRegisters = new ushort[holdingRegisterCount];
        _inputRegisters = new ushort[inputRegisterCount];
        _forcedDiscreteInputs = new bool[discreteInputCount];
        _forcedInputRegisters = new bool[inputRegisterCount];
    }

    /// <summary>Função que lê e reescreve os comandos dentro da trava da imagem.</summary>
    internal delegate void CommandRewriter(Span<bool> coils, Span<ushort> holdingRegisters);

    public int CoilCount => _coils.Length;

    public int DiscreteInputCount => _discreteInputs.Length;

    public int HoldingRegisterCount => _holdingRegisters.Length;

    public int InputRegisterCount => _inputRegisters.Length;

    /// <summary>
    /// Bytes de dados da imagem: 1 por coil, 2 por entrada discreta (valor e marca de forçado), 2 por holding
    /// register e 3 por input register (valor e marca de forçado).
    /// </summary>
    public int SizeBytes =>
        _coils.Length
        + _discreteInputs.Length + _forcedDiscreteInputs.Length
        + (sizeof(ushort) * _holdingRegisters.Length)
        + (sizeof(ushort) * _inputRegisters.Length) + _forcedInputRegisters.Length;

    public bool TryReadCoils(int start, Span<bool> destination) => TryRead(_coils, start, destination);

    public bool TryReadDiscreteInputs(int start, Span<bool> destination) => TryRead(_discreteInputs, start, destination);

    public bool TryReadHoldingRegisters(int start, Span<ushort> destination) => TryRead(_holdingRegisters, start, destination);

    public bool TryReadInputRegisters(int start, Span<ushort> destination) => TryRead(_inputRegisters, start, destination);

    public bool TryWriteCoils(int start, ReadOnlySpan<bool> values) => TryWrite(_coils, start, values);

    public bool TryWriteHoldingRegisters(int start, ReadOnlySpan<ushort> values) => TryWrite(_holdingRegisters, start, values);

    public bool TryWriteCoil(int address, bool value) => TryWrite(_coils, address, [value]);

    public bool TryWriteHoldingRegister(int address, ushort value) => TryWrite(_holdingRegisters, address, [value]);

    /// <summary>Cópia coerente das quatro áreas: nenhuma escrita acontece no meio dela.</summary>
    public IoSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new IoSnapshot(
                [.. _coils],
                [.. _discreteInputs],
                [.. _holdingRegisters],
                [.. _inputRegisters],
                [.. _forcedDiscreteInputs],
                [.. _forcedInputRegisters],
                _format,
                _wordOrder,
                _stepCount,
                _watchdogTripped);
        }
    }

    /// <summary>Copia os comandos (coils e holding registers) para a planta, no início do passo.</summary>
    internal void CopyCommands(Span<bool> coils, Span<ushort> holdingRegisters)
    {
        lock (_gate)
        {
            _coils.CopyTo(coils);
            _holdingRegisters.CopyTo(holdingRegisters);
        }
    }

    /// <summary>Lê e reescreve os comandos numa operação só (valores iniciais e troca de formato).</summary>
    internal void RewriteCommands(CommandRewriter rewriter)
    {
        lock (_gate)
        {
            rewriter(_coils, _holdingRegisters);
        }
    }

    /// <summary>Publica as medições de um passo, todas de uma vez.</summary>
    internal void Publish(
        ReadOnlySpan<bool> discreteInputs,
        ReadOnlySpan<ushort> inputRegisters,
        ReadOnlySpan<bool> forcedDiscreteInputs,
        ReadOnlySpan<bool> forcedInputRegisters,
        AnalogFormat format,
        WordOrder wordOrder,
        long stepCount,
        bool watchdogTripped)
    {
        lock (_gate)
        {
            discreteInputs.CopyTo(_discreteInputs);
            inputRegisters.CopyTo(_inputRegisters);
            forcedDiscreteInputs.CopyTo(_forcedDiscreteInputs);
            forcedInputRegisters.CopyTo(_forcedInputRegisters);
            _format = format;
            _wordOrder = wordOrder;
            _stepCount = stepCount;
            _watchdogTripped = watchdogTripped;
        }
    }

    private bool TryRead<T>(T[] area, int start, Span<T> destination)
    {
        if (!InRange(area.Length, start, destination.Length))
        {
            return false;
        }

        lock (_gate)
        {
            area.AsSpan(start, destination.Length).CopyTo(destination);
        }

        return true;
    }

    private bool TryWrite<T>(T[] area, int start, ReadOnlySpan<T> values)
    {
        if (!InRange(area.Length, start, values.Length))
        {
            return false;
        }

        lock (_gate)
        {
            values.CopyTo(area.AsSpan(start, values.Length));
        }

        return true;
    }

    private static bool InRange(int areaLength, int start, int count) =>
        start >= 0 && count >= 0 && start <= areaLength - count;
}
