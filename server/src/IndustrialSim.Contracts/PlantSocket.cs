using System.Text.Json.Serialization;

namespace IndustrialSim.Contracts;

/// <summary>
/// Protocolo do WebSocket da planta (D05, RNF09): <c>/api/plants/{id}/ws</c>, mensagens JSON com o campo
/// <c>type</c>. O cliente abre com <see cref="HelloMessage"/>; o servidor responde <see cref="WelcomeMessage"/>
/// ou, se a versão for outra, <see cref="IncompatibleMessage"/> e fecha. Depois chegam
/// <see cref="StateMessage"/> a cada período de estado e os eventos quando algo muda.
/// </summary>
public static class PlantSocketProtocol
{
    /// <summary>Versão do protocolo; muda a cada alteração incompatível das mensagens.</summary>
    public const int Version = 1;

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

/// <summary>Mensagem do servidor para o cliente.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WelcomeMessage), "welcome")]
[JsonDerivedType(typeof(IncompatibleMessage), "incompatible")]
[JsonDerivedType(typeof(SocketErrorMessage), "error")]
[JsonDerivedType(typeof(StateMessage), "state")]
[JsonDerivedType(typeof(ModbusStatusMessage), "modbus")]
[JsonDerivedType(typeof(AlarmsMessage), "alarms")]
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

/// <summary>Modbus da planta: enviado no início e sempre que pedidos, conexões ou autorizações mudam.</summary>
/// <param name="Port">Porta Modbus TCP da planta.</param>
/// <param name="UnitId">Unit ID a que a planta responde.</param>
/// <param name="AuthorizedMasters">IPs autorizados.</param>
/// <param name="PendingRequests">Pedidos de pareamento esperando resposta.</param>
/// <param name="Connections">Conexões abertas.</param>
public sealed record ModbusStatusMessage(
    int Port,
    int UnitId,
    IReadOnlyList<string> AuthorizedMasters,
    IReadOnlyList<PairingRequestResponse> PendingRequests,
    IReadOnlyList<MasterConnectionInfo> Connections) : ServerMessage;

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
