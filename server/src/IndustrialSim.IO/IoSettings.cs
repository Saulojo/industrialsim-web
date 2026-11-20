namespace IndustrialSim.IO;

/// <summary>
/// Configurações de I/O de uma planta, alteráveis com ela rodando (descritivo, "Configurações e diagnóstico da
/// planta"). Uma alteração vale a partir do passo seguinte.
/// </summary>
/// <param name="Format">Formato analógico dos registradores (D08).</param>
/// <param name="WordOrder">Ordem das palavras dos <c>float32</c>.</param>
/// <param name="WatchdogEnabled">Watchdog ligado; desligado por padrão.</param>
/// <param name="WatchdogTimeoutSeconds">Tempo sem mudança do heartbeat que dispara o watchdog, em segundos (&gt; 0).</param>
/// <param name="DiagnosticsEnabled">Bloco de diagnóstico em unidades de engenharia publicado; desligado, o bloco fica em zero.</param>
public sealed record IoSettings(
    AnalogFormat Format = AnalogFormat.Counts27648,
    WordOrder WordOrder = WordOrder.HighWordFirst,
    bool WatchdogEnabled = false,
    double WatchdogTimeoutSeconds = 2.0,
    bool DiagnosticsEnabled = true);

/// <summary>Um valor forçado (D30).</summary>
/// <param name="Area">Entrada discreta ou input register.</param>
/// <param name="Address">Endereço do sinal no cenário (no <c>float32</c>, o registrador é <c>2a</c>).</param>
/// <param name="Signal">Sinal (ou <c>watchdog</c>, para a entrada discreta do watchdog).</param>
/// <param name="Value">Valor em unidade de engenharia; 0 ou 1 nas entradas discretas.</param>
public sealed record ForcedInput(ModbusArea Area, int Address, string Signal, double Value);

/// <summary>Falha de um transmissor, que leva a saída à corrente de falha da NAMUR NE 43.</summary>
public enum TransmitterFault
{
    /// <summary>Sem falha: a corrente segue a medição.</summary>
    None,

    /// <summary>Corrente de falha baixa: 3,6 mA.</summary>
    Low,

    /// <summary>Corrente de falha alta: 21 mA.</summary>
    High,
}
