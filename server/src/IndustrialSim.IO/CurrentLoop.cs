namespace IndustrialSim.IO;

/// <summary>
/// Sinal de corrente 4–20 mA de um instrumento: a faixa (LRV/URV) corresponde a 4 e 20 mA.
/// Limites e correntes de falha conforme a NAMUR NE 43 ("Standardization of the Signal Level for the Failure
/// Information of Digital Transmitters"): a medição válida fica entre 3,8 e 20,5 mA; ≤ 3,6 mA ou ≥ 21 mA
/// indicam falha do instrumento.
/// </summary>
public static class CurrentLoop
{
    /// <summary>Corrente no início da faixa (LRV).</summary>
    public const double ZeroMilliamps = 4.0;

    /// <summary>Diferença de corrente entre o fim e o início da faixa.</summary>
    public const double SpanMilliamps = 16.0;

    /// <summary>Corrente no fim da faixa (URV).</summary>
    public const double FullScaleMilliamps = ZeroMilliamps + SpanMilliamps;

    /// <summary>Menor corrente de uma medição válida (NE 43).</summary>
    public const double SaturationLowMilliamps = 3.8;

    /// <summary>Maior corrente de uma medição válida (NE 43).</summary>
    public const double SaturationHighMilliamps = 20.5;

    /// <summary>Corrente de falha baixa (NE 43).</summary>
    public const double FaultLowMilliamps = 3.6;

    /// <summary>Corrente de falha alta (NE 43).</summary>
    public const double FaultHighMilliamps = 21.0;

    /// <summary>Corrente correspondente ao valor, pela faixa; não limita (ver <see cref="Saturate"/>).</summary>
    public static double ToMilliamps(double value, double lrv, double urv) =>
        ZeroMilliamps + (SpanMilliamps * (value - lrv) / (urv - lrv));

    /// <summary>Valor correspondente à corrente, pela faixa.</summary>
    public static double FromMilliamps(double milliamps, double lrv, double urv) =>
        lrv + ((milliamps - ZeroMilliamps) / SpanMilliamps * (urv - lrv));

    /// <summary>Limita a corrente à faixa de medição válida da NE 43 (3,8 a 20,5 mA).</summary>
    public static double Saturate(double milliamps) =>
        Math.Clamp(milliamps, SaturationLowMilliamps, SaturationHighMilliamps);
}
