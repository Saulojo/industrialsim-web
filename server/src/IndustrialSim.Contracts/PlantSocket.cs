using System.Text.Json.Serialization;

namespace IndustrialSim.Contracts;

/// <summary>
/// Protocolo do WebSocket da planta (D05, RNF09): <c>/api/plants/{id}/ws</c>, mensagens JSON com o campo
/// <c>type</c>. O cliente abre com <see cref="HelloMessage"/>; o servidor responde <see cref="WelcomeMessage"/>
/// ou, se a versão for outra, <see cref="IncompatibleMessage"/> e fecha. Depois chegam
/// <see cref="StateMessage"/> a cada período de estado e os eventos quando algo muda.
/// <para>
/// Versão 2 (Etapa 8): configurações, valores forçados, pausa, monitor de registradores e log de comunicação.
/// </para>
/// </summary>
public static class PlantSocketProtocol
{
    /// <summary>Versão do protocolo; muda a cada alteração incompatível das mensagens.</summary>
    public const int Version = 2;

    /// <summary>Caminho do WebSocket; <c>{id}</c> é o identificador da planta.</summary>
    public const string Path = "/api/plants/{id}/ws";
}

/// <summary>Mensagem do cliente (IHM) para o servidor.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(SetDisturbanceCommand), "setDisturbance")]
[JsonDerivedType(typeof(RestartCommand), "restart")]
[JsonDerivedType(typeof(AcceptPairingCommand), "acceptPairing")]
[JsonDerivedType(typeof(RefusePairingCommand), "refusePairing")]
[JsonDerivedType(typeof(RevokeMasterCommand), "revokeMaster")]
[JsonDerivedType(typeof(UpdateSettingsCommand), "updateSettings")]
[JsonDerivedType(typeof(ForceInputCommand), "forceInput")]
[JsonDerivedType(typeof(ReleaseInputCommand), "releaseInput")]
[JsonDerivedType(typeof(PauseCommand), "pause")]
[JsonDerivedType(typeof(ResumeCommand), "resume")]
[JsonDerivedType(typeof(SubscribeMonitorCommand), "subscribeMonitor")]
public abstract record ClientMessage;

/// <summary>Primeira mensagem do cliente.</summary>
/// <param name="ProtocolVersion">Versão do protocolo que o cliente fala (<see cref="PlantSocketProtocol.Version"/>).</param>
public sealed record HelloMessage(int ProtocolVersion) : ClientMessage;

/// <summary>Muda um distúrbio da seção <c>hmi</c> do cenário; vale no passo seguinte.</summary>
/// <param name="Input">Entrada externa (ex.: <c>FV-103_command</c>).</param>
/// <param name="Value">Valor na unidade da planta; limitado à faixa do cenário.</param>
public sealed record SetDisturbanceCommand(string Input, double Value) : ClientMessage;

/// <summary>Reinicia a planta no estado inicial do cenário, com a mesma semente (D41).</summary>
public sealed record RestartCommand : ClientMessage;

/// <summary>Aceita um pedido de pareamento de mestre Modbus (D38).</summary>
public sealed record AcceptPairingCommand(Guid RequestId) : ClientMessage;

/// <summary>Recusa um pedido de pareamento: fecha as conexões e bloqueia o IP por um tempo (D38).</summary>
public sealed record RefusePairingCommand(Guid RequestId) : ClientMessage;

/// <summary>Retira a autorização de um mestre e fecha as conexões dele (D38).</summary>
/// <param name="Address">IP do mestre.</param>
public sealed record RevokeMasterCommand(string Address) : ClientMessage;

/// <summary>
/// Muda as configurações de comunicação; só os campos não nulos mudam. Ou todas as mudanças valem, ou nenhuma.
/// As de I/O valem no passo seguinte; o Unit ID, na requisição seguinte.
/// </summary>
/// <param name="Format">Formato analógico (<see cref="AnalogFormats"/>).</param>
/// <param name="WordOrder">Ordem das palavras do <c>float32</c> (<see cref="WordOrders"/>).</param>
/// <param name="WatchdogEnabled">Watchdog ligado.</param>
/// <param name="WatchdogTimeoutSeconds">Tempo limite do watchdog, em segundos.</param>
/// <param name="DiagnosticsEnabled">Bloco de diagnóstico publicado.</param>
/// <param name="UnitId">Unit ID, de 1 a 255.</param>
/// <param name="MasterPolicy">Um mestre por vez ou vários (<see cref="MasterPolicies"/>, D42).</param>
public sealed record UpdateSettingsCommand(
    string? Format = null,
    string? WordOrder = null,
    bool? WatchdogEnabled = null,
    double? WatchdogTimeoutSeconds = null,
    bool? DiagnosticsEnabled = null,
    int? UnitId = null,
    string? MasterPolicy = null) : ClientMessage;

/// <summary>Força uma entrada que o mestre lê (D30); vale até ser liberada.</summary>
/// <param name="Area"><see cref="ModbusAreas.DiscreteInput"/> ou <see cref="ModbusAreas.InputRegister"/>.</param>
/// <param name="Signal">Sinal (ou <c>watchdog</c>, para a entrada discreta do watchdog).</param>
/// <param name="Value">Valor em unidade de engenharia; nas entradas discretas, 0 ou 1.</param>
public sealed record ForceInputCommand(string Area, string Signal, double Value) : ClientMessage;

/// <summary>Libera uma entrada forçada: ela volta a seguir a planta.</summary>
public sealed record ReleaseInputCommand(string Area, string Signal) : ClientMessage;

/// <summary>Pausa a planta: ela deixa de avançar, com o estado intacto (D43).</summary>
public sealed record PauseCommand : ClientMessage;

/// <summary>Retoma a planta pausada do mesmo estado.</summary>
public sealed record ResumeCommand : ClientMessage;

/// <summary>Liga ou desliga o envio do monitor de registradores para esta conexão (D44).</summary>
public sealed record SubscribeMonitorCommand(bool Enabled) : ClientMessage;

/// <summary>Mensagem do servidor para o cliente.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WelcomeMessage), "welcome")]
[JsonDerivedType(typeof(IncompatibleMessage), "incompatible")]
[JsonDerivedType(typeof(SocketErrorMessage), "error")]
[JsonDerivedType(typeof(StateMessage), "state")]
[JsonDerivedType(typeof(ModbusStatusMessage), "modbus")]
[JsonDerivedType(typeof(AlarmsMessage), "alarms")]
[JsonDerivedType(typeof(SettingsMessage), "settings")]
[JsonDerivedType(typeof(ForcedMessage), "forced")]
[JsonDerivedType(typeof(MonitorLayoutMessage), "monitorLayout")]
[JsonDerivedType(typeof(MonitorMessage), "monitor")]
[JsonDerivedType(typeof(LogMessage), "log")]
public abstract record ServerMessage;

/// <summary>Resposta ao <see cref="HelloMessage"/>: a planta e o que a IHM precisa para mostrá-la.</summary>
/// <param name="ProtocolVersion">Versão do protocolo do servidor.</param>
/// <param name="ServerVersion">Versão do servidor.</param>
/// <param name="Plant">Dados da planta.</param>
/// <param name="StepSeconds">Passo da simulação, em segundos.</param>
/// <param name="StatePeriodSeconds">Intervalo de tempo simulado entre duas <see cref="StateMessage"/>.</param>
/// <param name="ServerAddress">Endereço que o mestre usa para conectar; nulo para usar o host da página.</param>
/// <param name="Inputs">Entradas externas, na ordem de <see cref="StateMessage.Inputs"/>.</param>
/// <param name="Outputs">Saídas externas, na ordem de <see cref="StateMessage.Outputs"/>.</param>
/// <param name="Disturbances">Distúrbios que a IHM pode mudar.</param>
/// <param name="Alarms">Sinais que viram alarme (inclui <see cref="AlarmSignals.Watchdog"/>).</param>
public sealed record WelcomeMessage(
    int ProtocolVersion,
    string ServerVersion,
    PlantSummary Plant,
    double StepSeconds,
    double StatePeriodSeconds,
    string? ServerAddress,
    IReadOnlyList<SignalInfo> Inputs,
    IReadOnlyList<SignalInfo> Outputs,
    IReadOnlyList<DisturbanceInfo> Disturbances,
    IReadOnlyList<string> Alarms) : ServerMessage;

/// <summary>A versão do cliente não é a do servidor; o servidor fecha a conexão em seguida.</summary>
/// <param name="ServerProtocolVersion">Versão que o servidor fala.</param>
/// <param name="ClientProtocolVersion">Versão que o cliente pediu.</param>
public sealed record IncompatibleMessage(int ServerProtocolVersion, int ClientProtocolVersion) : ServerMessage;

/// <summary>Erro: só um código (<see cref="SocketErrorCodes"/>), traduzido pela interface (RNF11).</summary>
/// <param name="Code">Código do erro.</param>
/// <param name="Fatal">Se o servidor fecha a conexão depois desta mensagem.</param>
public sealed record SocketErrorMessage(string Code, bool Fatal) : ServerMessage;

/// <summary>Códigos de <see cref="SocketErrorMessage"/>.</summary>
public static class SocketErrorCodes
{
    public const string PlantNotFound = "plant-not-found";
    public const string PlantEnded = "plant-ended";
    public const string HelloExpected = "hello-expected";
    public const string InvalidMessage = "invalid-message";
    public const string DisturbanceNotFound = "disturbance-not-found";
    public const string PairingRequestNotFound = "pairing-request-not-found";
    public const string PlantNotRunning = "plant-not-running";
    public const string PlantNotPaused = "plant-not-paused";
    public const string InvalidSettings = "invalid-settings";
    public const string SeveralMastersAuthorized = "several-masters-authorized";
    public const string InputNotFound = "input-not-found";
    public const string MasterNotFound = "master-not-found";
}

/// <summary>Estado da planta num passo; valores em SI (D06), na ordem do <see cref="WelcomeMessage"/>.</summary>
/// <param name="Step">Passos simulados desde o último reinício.</param>
/// <param name="Time">Tempo simulado desde o último reinício, em segundos.</param>
/// <param name="Run">Número do reinício (0 até o primeiro); quando muda, a planta voltou ao estado inicial.</param>
/// <param name="Status">Situação da planta (<see cref="PlantStatuses"/>).</param>
/// <param name="Watchdog">Se o watchdog está disparado.</param>
/// <param name="Inputs">Entradas externas, já com o estado seguro do watchdog aplicado.</param>
/// <param name="Outputs">Saídas externas.</param>
public sealed record StateMessage(
    long Step,
    double Time,
    int Run,
    string Status,
    bool Watchdog,
    IReadOnlyList<float> Inputs,
    IReadOnlyList<float> Outputs) : ServerMessage;

/// <summary>
/// Modbus da planta: enviado no início e sempre que pedidos, conexões, autorizações ou contadores mudam (os
/// contadores são amostrados a cada segundo).
/// </summary>
/// <param name="Port">Porta Modbus TCP da planta.</param>
/// <param name="UnitId">Unit ID a que a planta responde.</param>
/// <param name="AuthorizedMasters">IPs autorizados.</param>
/// <param name="PendingRequests">Pedidos de pareamento esperando resposta.</param>
/// <param name="Connections">Conexões abertas.</param>
/// <param name="RequestsPerSecond">Requisições por segundo, medidas no último segundo.</param>
/// <param name="Requests">Requisições atendidas desde a criação da planta.</param>
/// <param name="ExceptionResponses">Respostas de exceção enviadas.</param>
public sealed record ModbusStatusMessage(
    int Port,
    int UnitId,
    IReadOnlyList<string> AuthorizedMasters,
    IReadOnlyList<PairingRequestResponse> PendingRequests,
    IReadOnlyList<MasterConnectionInfo> Connections,
    double RequestsPerSecond,
    long Requests,
    long ExceptionResponses) : ServerMessage;

/// <summary>Uma conexão de mestre, sem os contadores (que mudam a cada requisição).</summary>
/// <param name="Id">Número da conexão.</param>
/// <param name="Address">IP do mestre.</param>
/// <param name="Port">Porta de origem do mestre.</param>
/// <param name="ConnectedUtc">Abertura da conexão.</param>
/// <param name="Authorized">Se já passou pelo pareamento.</param>
public sealed record MasterConnectionInfo(long Id, string Address, int Port, DateTimeOffset ConnectedUtc, bool Authorized);

/// <summary>Alarmes ativos: enviado no início e sempre que algum entra ou sai.</summary>
public sealed record AlarmsMessage(IReadOnlyList<ActiveAlarm> Active) : ServerMessage;

/// <summary>Um alarme ativo.</summary>
/// <param name="Signal">Saída externa ou <see cref="AlarmSignals.Watchdog"/>.</param>
/// <param name="SinceTime">Tempo simulado em que ficou ativo, em segundos (desde o último reinício).</param>
/// <param name="SinceUtc">Horário em que ficou ativo.</param>
public sealed record ActiveAlarm(string Signal, double SinceTime, DateTimeOffset SinceUtc);

/// <summary>Sinais de alarme que não são saídas da planta.</summary>
public static class AlarmSignals
{
    /// <summary>Watchdog disparado: a planta está no estado seguro.</summary>
    public const string Watchdog = "watchdog";
}

/// <summary>Tipos de sinal em <see cref="SignalInfo"/>.</summary>
public static class SignalKinds
{
    public const string Analog = "analog";
    public const string Digital = "digital";
}

/// <summary>Uma entrada ou saída externa da planta, como a IHM a mostra.</summary>
/// <param name="Name">Nome (a tag); o nome de tela vem dos arquivos de idioma.</param>
/// <param name="Kind">Analógico ou digital (<see cref="SignalKinds"/>).</param>
/// <param name="Unit">Unidade de engenharia; vazia se não houver.</param>
/// <param name="UnitFactor">Valor de engenharia = valor em SI × fator (D29).</param>
/// <param name="Lrv">Início da faixa do instrumento, em SI; nulo se o sinal não tiver faixa.</param>
/// <param name="Urv">Fim da faixa do instrumento, em SI.</param>
/// <param name="Modbus">Ponto Modbus ligado ao sinal; nulo se não houver.</param>
public sealed record SignalInfo(
    string Name,
    string Kind,
    string Unit,
    double UnitFactor,
    double? Lrv,
    double? Urv,
    ModbusPointInfo? Modbus);

/// <summary>Endereço Modbus de um sinal.</summary>
/// <param name="Area">Área (<see cref="ModbusAreas"/>).</param>
/// <param name="Address">Endereço em base 0.</param>
/// <param name="ClassicAddress">Notação clássica (1, 10001, 30001, 40001…).</param>
public sealed record ModbusPointInfo(string Area, int Address, int ClassicAddress);

/// <summary>Distúrbio que a IHM pode mudar (seção <c>hmi</c> do cenário, D39).</summary>
/// <param name="Input">Entrada externa.</param>
/// <param name="Min">Menor valor, em SI.</param>
/// <param name="Max">Maior valor, em SI.</param>
/// <param name="Unit">Unidade de engenharia.</param>
/// <param name="UnitFactor">Valor de engenharia = valor em SI × fator.</param>
public sealed record DisturbanceInfo(string Input, double Min, double Max, string Unit, double UnitFactor);

/// <summary>Formatos analógicos (D08), em <see cref="PlantSettingsInfo.Format"/>.</summary>
public static class AnalogFormats
{
    public const string Counts27648 = "counts27648";
    public const string Counts10000 = "counts10000";
    public const string Counts32767 = "counts32767";
    public const string Float32 = "float32";
}

/// <summary>Ordem das palavras do <c>float32</c>, em <see cref="PlantSettingsInfo.WordOrder"/>.</summary>
public static class WordOrders
{
    /// <summary>Normal (big-endian).</summary>
    public const string HighWordFirst = "highWordFirst";

    /// <summary>Invertida (word swap).</summary>
    public const string LowWordFirst = "lowWordFirst";
}

/// <summary>Quantos mestres podem ser autorizados (D42), em <see cref="PlantSettingsInfo.MasterPolicy"/>.</summary>
public static class MasterPolicies
{
    public const string Multiple = "multiple";
    public const string Single = "single";
}

/// <summary>Configurações de comunicação de uma planta, como a tela de configurações as mostra.</summary>
/// <param name="Format">Formato analógico (<see cref="AnalogFormats"/>).</param>
/// <param name="WordOrder">Ordem das palavras do <c>float32</c> (<see cref="WordOrders"/>).</param>
/// <param name="WatchdogEnabled">Watchdog ligado.</param>
/// <param name="WatchdogTimeoutSeconds">Tempo limite do watchdog, em segundos.</param>
/// <param name="DiagnosticsEnabled">Bloco de diagnóstico publicado.</param>
/// <param name="UnitId">Unit ID.</param>
/// <param name="MasterPolicy">Um mestre por vez ou vários (<see cref="MasterPolicies"/>).</param>
public sealed record PlantSettingsInfo(
    string Format,
    string WordOrder,
    bool WatchdogEnabled,
    double WatchdogTimeoutSeconds,
    bool DiagnosticsEnabled,
    int UnitId,
    string MasterPolicy);

/// <summary>Configurações: enviadas no início e sempre que mudam (por qualquer cliente).</summary>
/// <param name="Settings">Configurações pedidas (as de I/O valem a partir do passo seguinte).</param>
/// <param name="MaxWatchdogTimeoutSeconds">Maior tempo limite do watchdog aceito.</param>
public sealed record SettingsMessage(PlantSettingsInfo Settings, double MaxWatchdogTimeoutSeconds) : ServerMessage;

/// <summary>Uma entrada forçada.</summary>
/// <param name="Area"><see cref="ModbusAreas.DiscreteInput"/> ou <see cref="ModbusAreas.InputRegister"/>.</param>
/// <param name="Signal">Sinal (ou <c>watchdog</c>).</param>
/// <param name="Value">Valor em unidade de engenharia; 0 ou 1 nas entradas discretas.</param>
public sealed record ForcedInputInfo(string Area, string Signal, double Value);

/// <summary>Entradas forçadas: enviadas no início e sempre que mudam.</summary>
public sealed record ForcedMessage(IReadOnlyList<ForcedInputInfo> Inputs) : ServerMessage;

/// <summary>O que um ponto do monitor representa, em <see cref="MonitorPointInfo.Kind"/>.</summary>
public static class MonitorPointKinds
{
    public const string Digital = "digital";
    public const string Analog = "analog";
    public const string Diagnostic = "diagnostic";
    public const string Heartbeat = "heartbeat";
    public const string WatchdogStatus = "watchdog-status";
}

/// <summary>Uma linha do monitor de registradores: um ponto do mapa de I/O.</summary>
/// <param name="Area">Área (<see cref="ModbusAreas"/>).</param>
/// <param name="Address">Primeiro registrador, em base 0, no formato atual.</param>
/// <param name="ClassicAddress">O mesmo endereço na notação clássica.</param>
/// <param name="Length">Registradores ocupados (2 no <c>float32</c>).</param>
/// <param name="Signal">Sinal (<c>heartbeat</c> e <c>watchdog</c> para os do watchdog).</param>
/// <param name="Unit">Unidade de engenharia; vazia se não se aplica.</param>
/// <param name="Kind">O que o ponto representa (<see cref="MonitorPointKinds"/>).</param>
public sealed record MonitorPointInfo(string Area, int Address, int ClassicAddress, int Length, string Signal, string Unit, string Kind);

/// <summary>
/// Linhas do monitor: enviadas ao ligar o monitor e sempre que o formato ou a ordem das palavras mudam (no
/// <c>float32</c> os endereços mudam, D27).
/// </summary>
/// <param name="Format">Formato em que os registradores estão (<see cref="AnalogFormats"/>).</param>
/// <param name="WordOrder">Ordem das palavras (<see cref="WordOrders"/>).</param>
/// <param name="Points">Linhas, na ordem do mapa: coils, entradas discretas, holding e input registers.</param>
public sealed record MonitorLayoutMessage(string Format, string WordOrder, IReadOnlyList<MonitorPointInfo> Points) : ServerMessage;

/// <summary>Última escrita num ponto do monitor.</summary>
/// <param name="Point">Índice da linha em <see cref="MonitorLayoutMessage.Points"/>.</param>
/// <param name="Author">Quem escreveu: o IP do mestre.</param>
/// <param name="TimeUtc">Horário da escrita.</param>
public sealed record MonitorWriteInfo(int Point, string Author, DateTimeOffset TimeUtc);

/// <summary>
/// Valores do monitor de registradores, a 10 Hz, só para as conexões que ligaram o monitor (D44).
/// </summary>
/// <param name="Step">Passo da planta a que os valores correspondem.</param>
/// <param name="Raw">Valor bruto: as palavras de 16 bits (ou o bit) de cada linha, em sequência (<see cref="MonitorPointInfo.Length"/> por linha).</param>
/// <param name="Values">Valor de engenharia de cada linha, como o mestre o obteria ao escalar.</param>
/// <param name="Forced">Índices das linhas com valor forçado.</param>
/// <param name="Writes">
/// Última escrita em cada linha escrita por um mestre; nula se nada mudou desde a mensagem anterior (o horário é
/// atualizado ao menos uma vez por segundo).
/// </param>
public sealed record MonitorMessage(
    long Step,
    IReadOnlyList<int> Raw,
    IReadOnlyList<float> Values,
    IReadOnlyList<int> Forced,
    IReadOnlyList<MonitorWriteInfo>? Writes) : ServerMessage;

/// <summary>Tipos de evento do log de comunicação; os argumentos de cada um estão no log do servidor.</summary>
public static class LogEventKinds
{
    public const string ConnectionAuthorized = "connection-authorized";
    public const string ConnectionRefused = "connection-refused";
    public const string ConnectionClosed = "connection-closed";
    public const string ConnectionLimit = "connection-limit";
    public const string PairingRequested = "pairing-requested";
    public const string PairingAccepted = "pairing-accepted";
    public const string PairingRefused = "pairing-refused";
    public const string PairingExpired = "pairing-expired";
    public const string MasterAuthorized = "master-authorized";
    public const string MasterRevoked = "master-revoked";
    public const string MasterReplaced = "master-replaced";
    public const string ModbusException = "modbus-exception";
    public const string SettingChanged = "setting-changed";
    public const string InputForced = "input-forced";
    public const string InputReleased = "input-released";
    public const string Paused = "paused";
    public const string Resumed = "resumed";
    public const string Restarted = "restarted";
}

/// <summary>Um argumento de um evento do log (texto estável, traduzido pela interface).</summary>
public sealed record LogArgumentInfo(string Name, string Value);

/// <summary>Um evento do log de comunicação.</summary>
/// <param name="Id">Identificador; um evento agrupado mantém o mesmo.</param>
/// <param name="Sequence">Ordem no log; cresce a cada ocorrência agrupada.</param>
/// <param name="Kind">Tipo (<see cref="LogEventKinds"/>).</param>
/// <param name="FirstUtc">Primeira ocorrência.</param>
/// <param name="TimeUtc">Última ocorrência.</param>
/// <param name="Count">Ocorrências agrupadas.</param>
/// <param name="Arguments">Argumentos do evento.</param>
public sealed record LogEntryInfo(
    long Id,
    long Sequence,
    string Kind,
    DateTimeOffset FirstUtc,
    DateTimeOffset TimeUtc,
    int Count,
    IReadOnlyList<LogArgumentInfo> Arguments);

/// <summary>
/// Log de comunicação: no início, todos os eventos guardados (<paramref name="Reset"/>); depois, os novos e os
/// agrupados de novo (um evento com o mesmo <see cref="LogEntryInfo.Id"/> substitui o anterior).
/// </summary>
public sealed record LogMessage(IReadOnlyList<LogEntryInfo> Entries, bool Reset) : ServerMessage;
