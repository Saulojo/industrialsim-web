using System.Globalization;
using System.Text;
using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;

namespace IndustrialSim.Sessions.Recording;

/// <summary>Dados do cabeçalho de uma gravação, necessários para reproduzi-la.</summary>
/// <param name="ScenarioId">Cenário gravado.</param>
/// <param name="NoiseSeed">Semente do ruído da planta (D22, D25).</param>
/// <param name="StepSeconds">Passo da planta.</param>
/// <param name="SampleEverySteps">Uma linha de medição a cada tantos passos.</param>
public sealed record RecordingHeader(string ScenarioId, ulong NoiseSeed, double StepSeconds, int SampleEverySteps);

/// <summary>
/// Grava um ensaio em CSV (RF07): os comandos que a planta recebeu, com o tempo simulado, e as medições.
/// </summary>
/// <remarks>
/// Formato (versão 1):
/// <list type="bullet">
/// <item>Cabeçalho: linhas <c>#chave=valor</c> com formato, cenário, semente, passo e amostragem.</item>
/// <item>Linha de colunas: <c>step</c>, <c>time_s</c>, coils (<c>coil_0</c>…) e holding registers (<c>hr_0</c>…)
/// brutos, como foram copiados da imagem no início do passo; configurações de I/O (<c>format</c>,
/// <c>word_order</c>, <c>watchdog</c>, <c>watchdog_timeout_s</c>, <c>diagnostics</c>); entradas externas da planta
/// (em SI, já com o watchdog aplicado); saídas externas (em SI); <c>watchdog_tripped</c>.</item>
/// <item>Uma linha por passo em que algum comando, configuração ou entrada mudou e uma linha a cada
/// <see cref="RecordingHeader.SampleEverySteps"/> passos. <c>step</c> é o número de passos concluídos e
/// <c>time_s</c> o tempo simulado no fim do passo: os comandos da linha valeram no passo que terminou ali, e as
/// saídas são o estado nesse instante.</item>
/// </list>
/// Números com ponto decimal e todos os dígitos necessários para reler o mesmo valor (formato "R"); booleanos
/// como 0 e 1. Com o cabeçalho e as linhas, <see cref="Replayer"/> refaz o ensaio passo a passo.
/// <para>
/// Usado pela thread do passo: <see cref="ICommandLatchObserver.OnCommandsLatched"/> na fase de comandos e
/// <see cref="OnStepCompleted"/> logo depois do passo.
/// </para>
/// </remarks>
public sealed class PlantRecorder : ICommandLatchObserver
{
    public const string FormatName = "industrialsim-recording/1";

    private readonly PlantIo _io;
    private readonly Action<string> _writeLine;
    private readonly StringBuilder _line = new();
    private bool[] _coils;
    private bool[] _previousCoils;
    private ushort[] _holding;
    private ushort[] _previousHolding;
    private double[] _inputs;
    private double[] _previousInputs;
    private IoSettings? _settings;
    private IoSettings? _previousSettings;
    private bool _changed;
    private bool _latched;

    /// <summary>Liga o gravador a <paramref name="io"/> e escreve o cabeçalho e a linha de colunas.</summary>
    /// <param name="io">Planta e imagem de I/O gravadas; ainda sem nenhum passo.</param>
    /// <param name="header">Dados do cabeçalho; o cenário e o passo devem ser os da planta.</param>
    /// <param name="writeLine">Recebe cada linha, sem o fim de linha.</param>
    public PlantRecorder(PlantIo io, RecordingHeader header, Action<string> writeLine)
    {
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(writeLine);
        ArgumentOutOfRangeException.ThrowIfLessThan(header.SampleEverySteps, 1, nameof(header));
        if (header.ScenarioId != io.Plant.Definition.Id || header.StepSeconds != io.Plant.Clock.StepSeconds)
        {
            throw new ArgumentException("O cabeçalho não corresponde à planta gravada.", nameof(header));
        }

        if (io.Plant.Clock.StepCount != 0)
        {
            throw new ArgumentException("A gravação começa antes do primeiro passo da planta.", nameof(io));
        }

        _io = io;
        _writeLine = writeLine;
        Header = header;
        Columns = BuildColumns(io);

        _coils = new bool[io.Image.CoilCount];
        _previousCoils = new bool[io.Image.CoilCount];
        _holding = new ushort[io.Image.HoldingRegisterCount];
        _previousHolding = new ushort[io.Image.HoldingRegisterCount];
        _inputs = new double[io.Plant.InputNames.Count];
        _previousInputs = new double[io.Plant.InputNames.Count];

        writeLine($"#format={FormatName}");
        writeLine($"#scenario={header.ScenarioId}");
        writeLine(string.Create(CultureInfo.InvariantCulture, $"#noise_seed={header.NoiseSeed}"));
        writeLine($"#step_s={Format(header.StepSeconds)}");
        writeLine(string.Create(CultureInfo.InvariantCulture, $"#sample_every_steps={header.SampleEverySteps}"));
        writeLine(string.Join(',', Columns));

        io.LatchObserver = this;
    }

    public RecordingHeader Header { get; }

    /// <summary>Nomes das colunas, na ordem do arquivo.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Linhas de dados escritas.</summary>
    public long RowsWritten { get; private set; }

    /// <summary>Colunas da gravação de uma planta (as mesmas para gravar e para reproduzir).</summary>
    public static IReadOnlyList<string> BuildColumns(PlantIo io)
    {
        ArgumentNullException.ThrowIfNull(io);
        ScenarioPlant plant = io.Plant;
        var columns = new List<string> { "step", "time_s" };
        columns.AddRange(Enumerable.Range(0, io.Image.CoilCount).Select(i => $"coil_{i}"));
        columns.AddRange(Enumerable.Range(0, io.Image.HoldingRegisterCount).Select(i => $"hr_{i}"));
        columns.AddRange(["format", "word_order", "watchdog", "watchdog_timeout_s", "diagnostics"]);
        columns.AddRange(plant.InputNames);
        columns.AddRange(plant.OutputNames);
        columns.Add("watchdog_tripped");

        var duplicated = columns.GroupBy(c => c, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicated.Count > 0)
        {
            throw new InvalidOperationException($"Colunas repetidas na gravação de '{plant.Definition.Id}': {string.Join(", ", duplicated)}.");
        }

        return columns;
    }

    void ICommandLatchObserver.OnCommandsLatched(long stepIndex, ReadOnlySpan<bool> coils, ReadOnlySpan<ushort> holdingRegisters, IoSettings settings)
    {
        coils.CopyTo(_coils);
        holdingRegisters.CopyTo(_holding);
        _settings = settings;
        ScenarioPlant plant = _io.Plant;
        for (int i = 0; i < _inputs.Length; i++)
        {
            _inputs[i] = plant.GetInput(i);
        }

        // O primeiro passo sempre entra; depois, só o que mudou em relação ao passo anterior.
        _changed = _previousSettings is null
            || !_coils.AsSpan().SequenceEqual(_previousCoils)
            || !_holding.AsSpan().SequenceEqual(_previousHolding)
            || !_inputs.AsSpan().SequenceEqual(_previousInputs)
            || settings != _previousSettings;
        _latched = true;
    }

    /// <summary>Depois do passo: escreve a linha, se houve mudança ou se é passo de amostragem.</summary>
    public void OnStepCompleted()
    {
        if (!_latched)
        {
            throw new InvalidOperationException("Passo sem a fase de comandos: o gravador não está ligado a esta planta.");
        }

        _latched = false;
        ScenarioPlant plant = _io.Plant;
        long step = plant.Clock.StepCount;
        if (_changed || step % Header.SampleEverySteps == 0)
        {
            WriteRow(step);
        }

        (_previousCoils, _coils) = (_coils, _previousCoils);
        (_previousHolding, _holding) = (_holding, _previousHolding);
        (_previousInputs, _inputs) = (_inputs, _previousInputs);
        _previousSettings = _settings;
    }

    internal static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private void WriteRow(long step)
    {
        ScenarioPlant plant = _io.Plant;
        IoSettings settings = _settings!;
        StringBuilder line = _line.Clear();
        line.Append(step.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Format(plant.Clock.TimeSeconds));
        foreach (bool coil in _coils)
        {
            line.Append(',').Append(coil ? '1' : '0');
        }

        foreach (ushort register in _holding)
        {
            line.Append(',').Append(register.ToString(CultureInfo.InvariantCulture));
        }

        line.Append(',').Append(settings.Format.ToString())
            .Append(',').Append(settings.WordOrder.ToString())
            .Append(',').Append(settings.WatchdogEnabled ? '1' : '0')
            .Append(',').Append(Format(settings.WatchdogTimeoutSeconds))
            .Append(',').Append(settings.DiagnosticsEnabled ? '1' : '0');

        foreach (double input in _inputs)
        {
            line.Append(',').Append(Format(input));
        }

        for (int i = 0; i < plant.OutputNames.Count; i++)
        {
            line.Append(',').Append(Format(plant.GetOutput(i)));
        }

        line.Append(',').Append(_io.WatchdogTripped ? '1' : '0');
        _writeLine(line.ToString());
        RowsWritten++;
    }
}
