using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.IO;

/// <summary>
/// Liga uma planta à sua imagem de I/O, como cartões de entrada e saída ligam um processo a um CLP.
/// </summary>
/// <remarks>
/// Cada passo (<see cref="Step"/>) tem três fases:
/// 1. <b>Comandos</b>: as configurações pendentes entram em vigor; coils e holding registers são copiados da
///    imagem, de uma vez, e convertidos em entradas da planta (contagens → mA → valor pela faixa). O watchdog é
///    avaliado e, se disparado, impõe o estado seguro. Uma escrita do mestre depois dessa cópia só vale no
///    passo seguinte (D06).
/// 2. <b>Planta</b>: avança um passo.
/// 3. <b>Medições</b>: as saídas da planta viram corrente (pela faixa LRV/URV, com os limites da NAMUR NE 43) e
///    depois contagens ou <c>float32</c>, no formato da planta, e são publicadas na imagem de uma vez.
/// O watchdog conta tempo simulado (passos), não tempo de relógio: o resultado é reproduzível (RNF02).
/// <para>
/// Threads: <see cref="Step"/> é chamado por uma única thread (a do laço da planta). Os métodos de configuração
/// (formato, valores forçados, faixa, falha) podem ser chamados de outras threads e valem a partir do passo
/// seguinte. A planta em si só é tocada pela thread do passo.
/// </para>
/// </remarks>
public sealed class PlantIo
{
    private readonly Lock _sync = new();
    private readonly IoImage.CommandRewriter _encodeInitialCommands;

    // Áreas de trabalho da thread do passo, do tamanho das áreas da imagem.
    private readonly bool[] _coils;
    private readonly ushort[] _holdingRegisters;
    private readonly bool[] _discreteInputs;
    private readonly ushort[] _inputRegisters;
    private readonly bool[] _forcedDiscreteFlags;
    private readonly bool[] _forcedRegisterFlags;

    // Estado por ponto, protegido por _sync.
    private readonly bool[] _discreteInputMapped;
    private readonly bool?[] _forcedDiscreteInputs;
    private readonly double?[] _forcedInputRegisters;
    private readonly double[] _lrv;
    private readonly double[] _urv;
    private readonly TransmitterFault[] _faults;
    private readonly Dictionary<int, int> _inputRegisterIndexes = [];

    private IoSettings _settings;
    private IoSettings? _pendingSettings;
    private ushort _lastHeartbeat;
    private int _staleSteps;
    private bool _watchdogTripped;

    /// <param name="plant">Planta montada de um cenário com a seção <c>io</c>.</param>
    /// <param name="settings">Configurações iniciais; por padrão, 0–27648, watchdog desligado e o tempo limite do cenário.</param>
    public PlantIo(ScenarioPlant plant, IoSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(plant);

        Plant = plant;
        Map = IoMap.Create(plant);
        _settings = Validate(settings ?? new IoSettings(WatchdogTimeoutSeconds: Map.DefaultWatchdogTimeoutSeconds));
        Image = new IoImage(Map.CoilCount, Map.DiscreteInputCount, Map.HoldingRegisterCount, Map.InputRegisterCount);

        _coils = new bool[Map.CoilCount];
        _holdingRegisters = new ushort[Map.HoldingRegisterCount];
        _discreteInputs = new bool[Map.DiscreteInputCount];
        _inputRegisters = new ushort[Map.InputRegisterCount];
        _forcedDiscreteFlags = new bool[Map.DiscreteInputCount];
        _forcedRegisterFlags = new bool[Map.InputRegisterCount];

        _discreteInputMapped = new bool[Map.DiscreteInputCount];
        _forcedDiscreteInputs = new bool?[Map.DiscreteInputCount];
        foreach (DigitalPoint point in Map.DiscreteInputs)
        {
            _discreteInputMapped[point.Address] = true;
        }

        if (Map.WatchdogStatusDiscreteInput is int status)
        {
            _discreteInputMapped[status] = true;
        }

        int count = Map.InputRegisters.Count;
        _forcedInputRegisters = new double?[count];
        _lrv = new double[count];
        _urv = new double[count];
        _faults = new TransmitterFault[count];
        for (int i = 0; i < count; i++)
        {
            AnalogPoint point = Map.InputRegisters[i];
            _inputRegisterIndexes.Add(point.Address, i);
            _lrv[i] = point.Lrv;
            _urv[i] = point.Urv;
        }

        // A imagem começa coerente com a planta: os comandos valem o que as entradas externas valem no
        // instante inicial, e as medições, o que a planta mostra antes do primeiro passo.
        _encodeInitialCommands = EncodeCommandsFromPlant;
        Image.RewriteCommands(_encodeInitialCommands);
        PublishInputs();
    }

    public ScenarioPlant Plant { get; }

    public IoMap Map { get; }

    public IoImage Image { get; }

    /// <summary>Configurações em vigor (as do último passo).</summary>
    public IoSettings Settings
    {
        get
        {
            lock (_sync)
            {
                return _settings;
            }
        }
    }

    /// <summary>Se o watchdog está disparado (planta no estado seguro).</summary>
    public bool WatchdogTripped
    {
        get
        {
            lock (_sync)
            {
                return _watchdogTripped;
            }
        }
    }

    /// <summary>
    /// Troca as configurações com a planta rodando; valem a partir do passo seguinte. Ao trocar o formato ou a
    /// ordem das palavras, os comandos que estão nos holding registers são regravados no novo formato, para a
    /// planta não levar um tranco enquanto o mestre não reescreve os seus valores.
    /// </summary>
    public void UpdateSettings(IoSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        lock (_sync)
        {
            _pendingSettings = settings;
        }
    }

    /// <summary>
    /// Força o input register do sinal de endereço <paramref name="address"/> (o endereço do cenário) num valor
    /// em unidade de engenharia (D30). O valor passa pela faixa e pelo formato atuais, como uma medição, e
    /// ignora a planta e a falha do transmissor até ser liberado. Falso se o endereço não for de um sinal.
    /// </summary>
    public bool ForceInputRegister(int address, double engineeringValue)
    {
        if (!double.IsFinite(engineeringValue))
        {
            throw new ArgumentOutOfRangeException(nameof(engineeringValue), engineeringValue, "O valor forçado deve ser finito.");
        }

        lock (_sync)
        {
            if (!_inputRegisterIndexes.TryGetValue(address, out int index))
            {
                return false;
            }

            _forcedInputRegisters[index] = engineeringValue;
            return true;
        }
    }

    /// <summary>Libera o input register forçado: ele volta a seguir a planta no passo seguinte.</summary>
    public bool ReleaseInputRegister(int address)
    {
        lock (_sync)
        {
            if (!_inputRegisterIndexes.TryGetValue(address, out int index))
            {
                return false;
            }

            _forcedInputRegisters[index] = null;
            return true;
        }
    }

    /// <summary>Força uma entrada discreta. Falso se o endereço não estiver no mapa.</summary>
    public bool ForceDiscreteInput(int address, bool value)
    {
        lock (_sync)
        {
            if (!IsDiscreteInput(address))
            {
                return false;
            }

            _forcedDiscreteInputs[address] = value;
            return true;
        }
    }

    /// <summary>Libera a entrada discreta forçada.</summary>
    public bool ReleaseDiscreteInput(int address)
    {
        lock (_sync)
        {
            if (!IsDiscreteInput(address))
            {
                return false;
            }

            _forcedDiscreteInputs[address] = null;
            return true;
        }
    }

    /// <summary>Libera todos os valores forçados.</summary>
    public void ReleaseAll()
    {
        lock (_sync)
        {
            Array.Clear(_forcedDiscreteInputs);
            Array.Clear(_forcedInputRegisters);
        }
    }

    /// <summary>Faixa atual (LRV, URV) do sinal de endereço <paramref name="address"/>, na unidade da planta.</summary>
    public (double Lrv, double Urv) GetRange(int address)
    {
        lock (_sync)
        {
            int index = InputRegisterIndex(address);
            return (_lrv[index], _urv[index]);
        }
    }

    /// <summary>
    /// Troca a faixa de um transmissor (o que o HART fará a partir da Etapa 16): muda a corrente e, portanto,
    /// as contagens que o mestre lê. Na unidade da planta; vale a partir do passo seguinte.
    /// </summary>
    public void SetRange(int address, double lrv, double urv)
    {
        if (!double.IsFinite(lrv) || !double.IsFinite(urv) || lrv == urv)
        {
            throw new ArgumentException($"Faixa inválida: LRV = {lrv}, URV = {urv}. Os dois devem ser finitos e diferentes.");
        }

        lock (_sync)
        {
            int index = InputRegisterIndex(address);
            _lrv[index] = lrv;
            _urv[index] = urv;
        }
    }

    /// <summary>Põe ou tira um transmissor em falha (corrente de 3,6 ou 21 mA).</summary>
    public void SetFault(int address, TransmitterFault fault)
    {
        lock (_sync)
        {
            _faults[InputRegisterIndex(address)] = fault;
        }
    }

    /// <summary>Avança um passo: comandos, planta, medições.</summary>
    public void Step()
    {
        LatchCommands();
        Plant.Step();
        PublishInputs();
    }

    /// <summary>Fase 1 do passo: copia os comandos da imagem para a planta e avalia o watchdog.</summary>
    internal void LatchCommands()
    {
        lock (_sync)
        {
            ApplyPendingSettings();
            Image.CopyCommands(_coils, _holdingRegisters);

            foreach (DigitalPoint point in Map.Coils)
            {
                Plant.SetInput(point.PlantIndex, _coils[point.Address] ? 1.0 : 0.0);
            }

            foreach (AnalogPoint point in Map.HoldingRegisters)
            {
                if (DecodeCommand(point, _holdingRegisters, _settings) is double value)
                {
                    Plant.SetInput(point.PlantIndex, value);
                }
            }

            UpdateWatchdog();
            if (_watchdogTripped)
            {
                foreach (SafeStatePoint safe in Map.SafeState)
                {
                    Plant.SetInput(safe.PlantIndex, safe.Value);
                }
            }
        }
    }

    /// <summary>Fase 3 do passo: converte as saídas da planta e publica na imagem.</summary>
    internal void PublishInputs()
    {
        lock (_sync)
        {
            IoSettings settings = _settings;
            Array.Clear(_discreteInputs);
            Array.Clear(_inputRegisters);
            Array.Clear(_forcedDiscreteFlags);
            Array.Clear(_forcedRegisterFlags);

            foreach (DigitalPoint point in Map.DiscreteInputs)
            {
                SetDiscreteInput(point.Address, Plant.GetOutput(point.PlantIndex) >= 0.5);
            }

            if (Map.WatchdogStatusDiscreteInput is int status)
            {
                SetDiscreteInput(status, _watchdogTripped);
            }

            bool isFloat = settings.Format == AnalogFormat.Float32;
            for (int i = 0; i < Map.InputRegisters.Count; i++)
            {
                AnalogPoint point = Map.InputRegisters[i];
                double lrv = _lrv[i];
                double urv = _urv[i];
                bool forced = _forcedInputRegisters[i].HasValue;

                double milliamps;
                if (_forcedInputRegisters[i] is double engineering)
                {
                    milliamps = CurrentLoop.Saturate(CurrentLoop.ToMilliamps(engineering / point.UnitFactor, lrv, urv));
                }
                else
                {
                    milliamps = _faults[i] switch
                    {
                        TransmitterFault.Low => CurrentLoop.FaultLowMilliamps,
                        TransmitterFault.High => CurrentLoop.FaultHighMilliamps,
                        _ => CurrentLoop.Saturate(CurrentLoop.ToMilliamps(Plant.GetOutput(point.PlantIndex), lrv, urv)),
                    };
                }

                int register = IoMap.RegisterAddress(point.Address, settings.Format);
                if (isFloat)
                {
                    // O que o mestre obteria ao escalar a corrente pela faixa, já em unidade de engenharia.
                    double value = CurrentLoop.FromMilliamps(milliamps, lrv, urv) * point.UnitFactor;
                    AnalogCodec.WriteFloat32((float)value, settings.WordOrder, _inputRegisters.AsSpan(register, 2));
                    _forcedRegisterFlags[register] = forced;
                    _forcedRegisterFlags[register + 1] = forced;
                }
                else
                {
                    _inputRegisters[register] = AnalogCodec.ToRegister(AnalogCodec.MilliampsToCounts(milliamps, settings.Format));
                    _forcedRegisterFlags[register] = forced;
                }
            }

            if (settings.DiagnosticsEnabled && Map.DiagnosticsAddress is int diagnostics)
            {
                for (int k = 0; k < Map.Diagnostics.Count; k++)
                {
                    DiagnosticPoint point = Map.Diagnostics[k];
                    float value = (float)(Plant.GetOutput(point.PlantIndex) * point.UnitFactor);
                    AnalogCodec.WriteFloat32(value, settings.WordOrder, _inputRegisters.AsSpan(diagnostics + (2 * k), 2));
                }
            }

            Image.Publish(
                _discreteInputs,
                _inputRegisters,
                _forcedDiscreteFlags,
                _forcedRegisterFlags,
                settings.Format,
                settings.WordOrder,
                Plant.Clock.StepCount,
                _watchdogTripped);
        }
    }

    private static IoSettings Validate(IoSettings settings)
    {
        if (!Enum.IsDefined(settings.Format))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Format, "Formato analógico desconhecido.");
        }

        if (!Enum.IsDefined(settings.WordOrder))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.WordOrder, "Ordem de palavras desconhecida.");
        }

        if (!double.IsFinite(settings.WatchdogTimeoutSeconds) || settings.WatchdogTimeoutSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.WatchdogTimeoutSeconds,
                "O tempo limite do watchdog deve ser finito e maior que zero.");
        }

        return settings;
    }

    /// <summary>
    /// Valor da entrada da planta que o registrador comanda, limitado à faixa do comando. Nulo se o registrador
    /// não for um número (<c>float32</c> NaN): a entrada fica como estava.
    /// </summary>
    private static double? DecodeCommand(AnalogPoint point, ReadOnlySpan<ushort> holdingRegisters, IoSettings settings)
    {
        int register = IoMap.RegisterAddress(point.Address, settings.Format);
        double value;
        if (settings.Format == AnalogFormat.Float32)
        {
            float engineering = AnalogCodec.ReadFloat32(holdingRegisters.Slice(register, 2), settings.WordOrder);
            if (float.IsNaN(engineering))
            {
                return null;
            }

            value = engineering / point.UnitFactor;
        }
        else
        {
            short counts = AnalogCodec.FromRegister(holdingRegisters[register]);
            value = CurrentLoop.FromMilliamps(AnalogCodec.CountsToMilliamps(counts, settings.Format), point.Lrv, point.Urv);
        }

        return Math.Clamp(value, Math.Min(point.Lrv, point.Urv), Math.Max(point.Lrv, point.Urv));
    }

    private static void EncodeCommand(AnalogPoint point, double value, Span<ushort> holdingRegisters, IoSettings settings)
    {
        int register = IoMap.RegisterAddress(point.Address, settings.Format);
        if (settings.Format == AnalogFormat.Float32)
        {
            AnalogCodec.WriteFloat32((float)(value * point.UnitFactor), settings.WordOrder, holdingRegisters.Slice(register, 2));
        }
        else
        {
            double milliamps = CurrentLoop.ToMilliamps(value, point.Lrv, point.Urv);
            holdingRegisters[register] = AnalogCodec.ToRegister(AnalogCodec.MilliampsToCounts(milliamps, settings.Format));
        }
    }

    private void ApplyPendingSettings()
    {
        if (_pendingSettings is not { } next)
        {
            return;
        }

        _pendingSettings = null;
        IoSettings previous = _settings;
        if (next.Format != previous.Format || next.WordOrder != previous.WordOrder)
        {
            // Dentro da trava da imagem: lê os comandos no formato antigo e regrava no novo, sem que uma
            // escrita do mestre caia no meio.
            Image.RewriteCommands((_, holding) =>
            {
                double?[] decoded = new double?[Map.HoldingRegisters.Count];
                for (int i = 0; i < decoded.Length; i++)
                {
                    decoded[i] = DecodeCommand(Map.HoldingRegisters[i], holding, previous);
                }

                holding[..Map.HoldingAnalogLength].Clear();
                for (int i = 0; i < decoded.Length; i++)
                {
                    AnalogPoint point = Map.HoldingRegisters[i];
                    EncodeCommand(point, decoded[i] ?? Plant.GetInput(point.PlantIndex), holding, next);
                }
            });
        }

        if (next.WatchdogEnabled != previous.WatchdogEnabled || next.WatchdogTimeoutSeconds != previous.WatchdogTimeoutSeconds)
        {
            _staleSteps = 0;
        }

        _settings = next;
    }

    private void EncodeCommandsFromPlant(Span<bool> coils, Span<ushort> holdingRegisters)
    {
        foreach (DigitalPoint point in Map.Coils)
        {
            coils[point.Address] = Plant.GetInput(point.PlantIndex) >= 0.5;
        }

        foreach (AnalogPoint point in Map.HoldingRegisters)
        {
            EncodeCommand(point, Plant.GetInput(point.PlantIndex), holdingRegisters, _settings);
        }
    }

    /// <summary>
    /// Conta os passos seguidos em que o heartbeat não mudou. Com o watchdog ligado, ao completar o tempo limite
    /// a planta vai ao estado seguro; quando o heartbeat muda, os comandos do mestre voltam a valer.
    /// </summary>
    private void UpdateWatchdog()
    {
        if (Map.HeartbeatRegister is not int register)
        {
            return;
        }

        ushort heartbeat = _holdingRegisters[register];
        if (!_settings.WatchdogEnabled || heartbeat != _lastHeartbeat)
        {
            _staleSteps = 0;
            _watchdogTripped = false;
        }
        else
        {
            int limit = Math.Max(1, (int)Math.Ceiling((_settings.WatchdogTimeoutSeconds / Plant.Clock.StepSeconds) - 1e-9));
            if (_staleSteps < limit)
            {
                _staleSteps++;
            }

            _watchdogTripped = _staleSteps >= limit;
        }

        _lastHeartbeat = heartbeat;
    }

    private void SetDiscreteInput(int address, bool plantValue)
    {
        bool? forced = _forcedDiscreteInputs[address];
        _discreteInputs[address] = forced ?? plantValue;
        _forcedDiscreteFlags[address] = forced.HasValue;
    }

    private bool IsDiscreteInput(int address) =>
        address >= 0 && address < _discreteInputMapped.Length && _discreteInputMapped[address];

    private int InputRegisterIndex(int address) =>
        _inputRegisterIndexes.TryGetValue(address, out int index)
            ? index
            : throw new KeyNotFoundException(
                $"O mapa não tem sinal de input register no endereço {address}. Disponíveis: {string.Join(", ", _inputRegisterIndexes.Keys)}.");
}
