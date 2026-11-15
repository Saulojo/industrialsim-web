namespace IndustrialSim.IO;

/// <summary>
/// Formato dos sinais analógicos nos registradores, escolhido por planta (D08).
/// Nos três formatos inteiros, 4 mA é 0 e 20 mA é o fim de escala; o registrador é lido como <c>INT</c> (16 bits com sinal).
/// </summary>
public enum AnalogFormat
{
    /// <summary>0–27648, igual ao cartão analógico Siemens (padrão).</summary>
    Counts27648,

    /// <summary>0–10000.</summary>
    Counts10000,

    /// <summary>0–32767.</summary>
    Counts32767,

    /// <summary><c>float32</c> (IEEE 754) em unidades de engenharia, em 2 registradores por sinal (D27).</summary>
    Float32,
}

/// <summary>Ordem das duas palavras de 16 bits de um <c>float32</c>.</summary>
public enum WordOrder
{
    /// <summary>Normal (big-endian): a palavra mais significativa no primeiro registrador.</summary>
    HighWordFirst,

    /// <summary>Invertida (word swap): a palavra menos significativa no primeiro registrador.</summary>
    LowWordFirst,
}
