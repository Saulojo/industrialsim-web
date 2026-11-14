namespace HartPoc;

/// <summary>
/// Códigos de unidade de pressão do HART (HCF_SPEC-183 r22.0, tabela de unidades, "Pressure")
/// e o fator para kPa. Fatores exatos por definição ou do NIST SP 811 (2008), apêndice B.8;
/// as colunas de água a 68 °F usam o valor convencional de 248,641 Pa por polegada.
/// </summary>
public static class PressureUnits
{
    public const byte InchesOfWater68F = 1;
    public const byte MillimetersOfWater68F = 4;
    public const byte MillimetersOfMercury0C = 5;
    public const byte Psi = 6;
    public const byte Bar = 7;
    public const byte Millibar = 8;
    public const byte KilogramsPerSquareCentimeter = 10;
    public const byte Pascal = 11;
    public const byte Kilopascal = 12;
    public const byte Torr = 13;
    public const byte Atmosphere = 14;
    public const byte Megapascal = 237;

    /// <summary>Quantos kPa vale uma unidade do código dado; falso se o código não é de pressão ou não é aceito.</summary>
    public static bool TryGetKpaPerUnit(byte unitCode, out double kpaPerUnit)
    {
        kpaPerUnit = unitCode switch
        {
            InchesOfWater68F => 0.248641,
            MillimetersOfWater68F => 0.248641 / 25.4,
            MillimetersOfMercury0C => 0.1333224,
            Psi => 6.894757,
            Bar => 100.0,
            Millibar => 0.1,
            KilogramsPerSquareCentimeter => 98.0665,
            Pascal => 0.001,
            Kilopascal => 1.0,
            Torr => 101.325 / 760.0,
            Atmosphere => 101.325,
            Megapascal => 1000.0,
            _ => double.NaN,
        };
        return !double.IsNaN(kpaPerUnit);
    }
}
