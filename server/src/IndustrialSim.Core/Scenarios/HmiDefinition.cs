namespace IndustrialSim.Core.Scenarios;

/// <summary>
/// Seção <c>hmi</c> do cenário (D39): o que a IHM web pode comandar e o que ela mostra como alarme. Os nomes de
/// tela vêm dos arquivos de idioma; aqui ficam só os sinais.
/// </summary>
/// <param name="Disturbances">Entradas externas que o operador muda pela IHM (cargas e distúrbios).</param>
/// <param name="Alarms">Saídas externas que viram alarme quando ativas (≥ 0,5).</param>
public sealed record HmiDefinition(
    IReadOnlyList<DisturbanceDefinition> Disturbances,
    IReadOnlyList<AlarmDefinition> Alarms);

/// <summary>
/// Distúrbio comandado pela IHM: uma entrada externa que o mapa Modbus não comanda, limitada a
/// [<paramref name="Min"/>, <paramref name="Max"/>] na unidade da planta. A interface mostra o valor de engenharia
/// (valor × <paramref name="UnitFactor"/>, em <paramref name="Unit"/>), como no mapa de I/O (D29).
/// </summary>
public sealed record DisturbanceDefinition(string Input, double Min, double Max, string Unit, double UnitFactor);

/// <summary>Alarme: a saída externa <paramref name="Signal"/> ativa (≥ 0,5) é uma condição anormal.</summary>
public sealed record AlarmDefinition(string Signal);
