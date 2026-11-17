using IndustrialSim.Core.Scenarios;
using IndustrialSim.IO;
using IndustrialSim.Sessions.Modbus;
using IndustrialSim.Sessions.RealTime;
using IndustrialSim.Sessions.Recording;
using Microsoft.Extensions.Logging;

namespace IndustrialSim.Sessions;

/// <summary>Situação de uma planta em execução.</summary>
public enum InstanceStatus
{
    /// <summary>Avançando em tempo real.</summary>
    Running,

    /// <summary>Parou por uma exceção no passo; continua listada até ser encerrada ou expirar.</summary>
    Faulted,

    /// <summary>Encerrada: fora do laço e do gerenciador.</summary>
    Ended,
}

/// <summary>Por que uma planta foi encerrada.</summary>
public enum EndReason
{
    /// <summary>A pedido (API ou código).</summary>
    Requested,

    /// <summary>Ficou sem cliente pelo tempo configurado.</summary>
    Expired,

    /// <summary>O servidor está encerrando.</summary>
    Shutdown,
}

/// <summary>Valor de um sinal externo da planta.</summary>
public readonly record struct SignalValue(string Name, double Value);

/// <summary>Estado de uma planta num instante: entradas e saídas externas, em SI (D06), do mesmo passo.</summary>
/// <param name="Status">Situação da planta.</param>
/// <param name="StepCount">Passos simulados.</param>
/// <param name="SimulatedSeconds">Tempo simulado.</param>
/// <param name="WatchdogTripped">Se o watchdog está disparado.</param>
/// <param name="Inputs">Entradas externas, na ordem do cenário.</param>
/// <param name="Outputs">Saídas externas, na ordem do cenário.</param>
public sealed record PlantState(
    InstanceStatus Status,
    long StepCount,
    double SimulatedSeconds,
    bool WatchdogTripped,
    IReadOnlyList<SignalValue> Inputs,
    IReadOnlyList<SignalValue> Outputs);

/// <summary>
/// Uma planta em execução no servidor: a planta do cenário, a sua imagem de I/O e os dados de quem a usa.
/// </summary>
/// <remarks>
/// O passo roda na thread do laço, sob a trava da instância; a leitura do estado usa a mesma trava, então vê
/// sempre um passo completo. Depois de <see cref="InstanceManager.End"/>, nenhum passo começa.
/// <para>
/// "Cliente" é quem mantém a planta em uso (o WebSocket da Etapa 7 e os mestres Modbus autorizados), por
/// <see cref="AttachClient"/>. Sem cliente, a planta expira depois do tempo configurado desde a última
/// atividade (D14; até a Etapa 10, expirar é encerrar).
/// </para>
/// </remarks>
public sealed class PlantInstance : IRealTimeStepper
{
    private readonly Lock _sync = new();
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly HashSet<string> _participants = new(StringComparer.Ordinal);
    private InstanceStatus _status = InstanceStatus.Running;
    private EndReason? _endReason;
    private string? _faultMessage;
    private int _clientCount;
    private DateTimeOffset _lastActivityUtc;
    private readonly RecordingFile? _recordingFile;
    private readonly PlantRecorder? _recorder;
    private LoopRegistration? _registration;

    /// <param name="recording">Onde gravar o ensaio e de quanto em quanto amostrar; nulo para não gravar.</param>
    /// <param name="startModbus">
    /// Cria o Modbus da planta, já escutando numa porta; nulo se não houver porta livre, e então a planta não é
    /// criada (<see cref="ModbusPortsExhaustedException"/>). Roda antes de a gravação abrir o arquivo.
    /// </param>
    internal PlantInstance(
        Guid id,
        ScenarioEntry scenario,
        ulong noiseSeed,
        string? ownerId,
        (string Path, int SampleEverySteps)? recording,
        Func<PlantInstance, PlantModbus?> startModbus,
        TimeProvider time,
        ILogger logger)
    {
        Id = id;
        Scenario = scenario;
        NoiseSeed = noiseSeed;
        OwnerId = ownerId;
        _time = time;
        _logger = logger;
        Io = new PlantIo(ScenarioPlant.Create(scenario.Definition, noiseSeed: noiseSeed));
        CreatedUtc = time.GetUtcNow();
        _lastActivityUtc = CreatedUtc;
        Modbus = startModbus(this) ?? throw new ModbusPortsExhaustedException();

        if (recording is var (path, sampleEverySteps))
        {
            _recordingFile = new RecordingFile(path, logger);
            var header = new RecordingHeader(scenario.Id, noiseSeed, scenario.Definition.StepSeconds, sampleEverySteps);
            _recorder = new PlantRecorder(Io, header, _recordingFile.WriteLine);
        }
    }

    /// <summary>Arquivo CSV da gravação do ensaio (RF07); nulo se a planta não grava.</summary>
    public string? RecordingPath => _recordingFile?.Path;

    public Guid Id { get; }

    public ScenarioEntry Scenario { get; }

    public string ScenarioId => Scenario.Id;

    /// <summary>Semente do ruído dos instrumentos (D22, D25).</summary>
    public ulong NoiseSeed { get; }

    /// <summary>Dono da planta; nulo para visitante. Usado a partir da Etapa 10.</summary>
    public string? OwnerId { get; }

    public DateTimeOffset CreatedUtc { get; }

    /// <summary>Imagem de I/O e configurações; é o que o Modbus (Etapa 6) e a IHM (Etapas 7 e 8) usam.</summary>
    public PlantIo Io { get; }

    /// <summary>Servidor Modbus da planta: porta, Unit ID, pareamento e conexões dos mestres.</summary>
    public PlantModbus Modbus { get; }

    /// <summary>Nome nos logs do laço.</summary>
    public string Name => $"Planta {Id}";

    public InstanceStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public EndReason? EndReason
    {
        get
        {
            lock (_sync)
            {
                return _endReason;
            }
        }
    }

    /// <summary>Mensagem da exceção que parou a planta, se ela estiver em falha.</summary>
    public string? FaultMessage
    {
        get
        {
            lock (_sync)
            {
                return _faultMessage;
            }
        }
    }

    public int ClientCount
    {
        get
        {
            lock (_sync)
            {
                return _clientCount;
            }
        }
    }

    /// <summary>Última atividade: criação, <see cref="Touch"/> ou saída de um cliente.</summary>
    public DateTimeOffset LastActivityUtc
    {
        get
        {
            lock (_sync)
            {
                return _lastActivityUtc;
            }
        }
    }

    /// <summary>Participantes além do dono (operadores e observadores), usados a partir da Etapa 11.</summary>
    public IReadOnlyList<string> Participants
    {
        get
        {
            lock (_sync)
            {
                return [.. _participants.Order(StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>Quanto a planta acompanhou o relógio (deriva, RNF05).</summary>
    public RealTimeTracking Tracking
    {
        get
        {
            LoopRegistration? registration;
            lock (_sync)
            {
                registration = _registration;
            }

            return registration?.GetTracking() ?? new RealTimeTracking(0, 0.0, 0.0, 0.0);
        }
    }

    /// <summary>Thread do laço que avança a planta.</summary>
    public int LoopThreadIndex
    {
        get
        {
            lock (_sync)
            {
                return _registration?.ThreadIndex ?? -1;
            }
        }
    }

    public bool AddParticipant(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        lock (_sync)
        {
            return _participants.Add(userId);
        }
    }

    public bool RemoveParticipant(string userId)
    {
        lock (_sync)
        {
            return _participants.Remove(userId);
        }
    }

    /// <summary>Marca atividade agora (por exemplo, uma consulta pela API).</summary>
    public void Touch()
    {
        lock (_sync)
        {
            _lastActivityUtc = _time.GetUtcNow();
        }
    }

    /// <summary>
    /// Registra um cliente; enquanto houver algum, a planta não expira. Descartar o objeto devolvido tira o
    /// cliente e conta como atividade.
    /// </summary>
    public IDisposable AttachClient()
    {
        lock (_sync)
        {
            _clientCount++;
            _lastActivityUtc = _time.GetUtcNow();
        }

        return new ClientLease(this);
    }

    public PlantState GetState()
    {
        lock (_sync)
        {
            ScenarioPlant plant = Io.Plant;
            var inputs = new SignalValue[plant.InputNames.Count];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = new SignalValue(plant.InputNames[i], plant.GetInput(i));
            }

            var outputs = new SignalValue[plant.OutputNames.Count];
            for (int i = 0; i < outputs.Length; i++)
            {
                outputs[i] = new SignalValue(plant.OutputNames[i], plant.GetOutput(i));
            }

            return new PlantState(_status, plant.Clock.StepCount, plant.Clock.TimeSeconds, Io.WatchdogTripped, inputs, outputs);
        }
    }

    void IRealTimeStepper.Step()
    {
        lock (_sync)
        {
            if (_status != InstanceStatus.Running)
            {
                return;
            }

            Io.Step();
            _recorder?.OnStepCompleted();
        }
    }

    void IRealTimeStepper.OnFault(Exception exception)
    {
        lock (_sync)
        {
            if (_status != InstanceStatus.Running)
            {
                return;
            }

            _status = InstanceStatus.Faulted;
            _faultMessage = exception.Message;
        }

        SessionsLog.InstanceFaulted(_logger, Id, exception.Message);
    }

    internal void AttachToLoop(LoopRegistration registration)
    {
        lock (_sync)
        {
            _registration = registration;
        }
    }

    /// <summary>
    /// Fecha o Modbus, tira a planta do laço e fecha a gravação. Ao retornar, nenhum mestre está conectado,
    /// nenhum passo novo começa e o arquivo da gravação está completo. Falso se já estava encerrada.
    /// </summary>
    internal bool End(EndReason reason)
    {
        LoopRegistration? registration;
        lock (_sync)
        {
            if (_status == InstanceStatus.Ended)
            {
                return false;
            }

            _status = InstanceStatus.Ended;
            _endReason = reason;
            registration = _registration;
        }

        Modbus.Dispose();
        registration?.Dispose();
        if (_recordingFile is not null && !_recordingFile.Complete(RecordingFlushTimeout))
        {
            SessionsLog.RecordingNotFlushed(_logger, _recordingFile.Path, RecordingFlushTimeout.TotalSeconds);
        }

        return true;
    }

    private static readonly TimeSpan RecordingFlushTimeout = TimeSpan.FromSeconds(5);

    private void DetachClient()
    {
        lock (_sync)
        {
            _clientCount--;
            _lastActivityUtc = _time.GetUtcNow();
        }
    }

    private sealed class ClientLease(PlantInstance owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.DetachClient();
            }
        }
    }
}

/// <summary>Nenhuma porta Modbus livre na faixa: a planta não pode ser criada.</summary>
internal sealed class ModbusPortsExhaustedException() : Exception("Nenhuma porta Modbus livre na faixa configurada.");
