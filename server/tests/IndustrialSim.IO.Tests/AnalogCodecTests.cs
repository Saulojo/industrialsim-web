namespace IndustrialSim.IO.Tests;

/// <summary>Critérios 1, 2 e 3 da Etapa 4: conversões entre valor, corrente, contagens e <c>float32</c>.</summary>
public class AnalogCodecTests(ITestOutputHelper output)
{
    public static TheoryData<AnalogFormat> IntegerFormats =>
        [AnalogFormat.Counts27648, AnalogFormat.Counts10000, AnalogFormat.Counts32767];

    /// <summary>
    /// Critério 1 (do lado do CLP): toda contagem representável, convertida em corrente, em valor pela faixa e
    /// de volta, retorna com erro de no máximo 1 contagem. Varre a faixa inteira do formato, com duas faixas de instrumento.
    /// </summary>
    [Theory]
    [MemberData(nameof(IntegerFormats))]
    public void CountsRoundTrip_ErrsAtMostOneCount_OverTheWholeRange(AnalogFormat format)
    {
        (double Lrv, double Urv)[] ranges = [(0.0, 1.2), (-2.5e5, 1.6e6)];
        int worst = 0;
        int tested = 0;

        foreach ((double lrv, double urv) in ranges)
        {
            for (int counts = AnalogCodec.MinCounts(format); counts <= AnalogCodec.MaxCounts(format); counts++)
            {
                double milliamps = AnalogCodec.CountsToMilliamps((short)counts, format);
                double value = CurrentLoop.FromMilliamps(milliamps, lrv, urv);
                short back = AnalogCodec.MilliampsToCounts(CurrentLoop.ToMilliamps(value, lrv, urv), format);

                worst = Math.Max(worst, Math.Abs(back - counts));
                tested++;
            }
        }

        output.WriteLine($"{format}: {tested} contagens varridas, maior erro = {worst} contagem(ns)");
        Assert.True(worst <= 1, $"maior erro: {worst}");
    }

    /// <summary>
    /// Critério 1 (do lado da planta): um valor dentro da faixa, levado a contagens e de volta, erra no máximo
    /// o equivalente a 1 contagem. Varredura fina da faixa (100 001 pontos).
    /// </summary>
    [Theory]
    [MemberData(nameof(IntegerFormats))]
    public void ValueRoundTrip_ErrsAtMostOneCount_OverTheWholeRange(AnalogFormat format)
    {
        const double Lrv = 0.0;
        const double Urv = 1.6e6;
        const int Points = 100_000;
        double oneCount = (Urv - Lrv) / AnalogCodec.FullScale(format);
        double worst = 0.0;

        for (int i = 0; i <= Points; i++)
        {
            double value = Lrv + ((Urv - Lrv) * i / Points);
            short counts = AnalogCodec.MilliampsToCounts(CurrentLoop.ToMilliamps(value, Lrv, Urv), format);
            double back = CurrentLoop.FromMilliamps(AnalogCodec.CountsToMilliamps(counts, format), Lrv, Urv);
            worst = Math.Max(worst, Math.Abs(back - value) / oneCount);
        }

        output.WriteLine($"{format}: maior erro = {worst:F4} contagem");
        Assert.True(worst <= 1.0, $"maior erro: {worst} contagem");
    }

    [Theory]
    [InlineData(AnalogFormat.Counts27648, 4.0, 0)]
    [InlineData(AnalogFormat.Counts27648, 12.0, 13824)]
    [InlineData(AnalogFormat.Counts27648, 20.0, 27648)]
    [InlineData(AnalogFormat.Counts10000, 4.0, 0)]
    [InlineData(AnalogFormat.Counts10000, 12.0, 5000)]
    [InlineData(AnalogFormat.Counts10000, 20.0, 10000)]
    [InlineData(AnalogFormat.Counts32767, 4.0, 0)]
    [InlineData(AnalogFormat.Counts32767, 20.0, 32767)]
    public void RangeEnds_MapToZeroAndFullScale(AnalogFormat format, double milliamps, int expected) =>
        Assert.Equal(expected, AnalogCodec.MilliampsToCounts(milliamps, format));

    /// <summary>Critério 2: correntes de falha NAMUR NE 43 no formato 0–27648, e o valor lido como <c>INT</c>.</summary>
    [Theory]
    [InlineData(CurrentLoop.FaultLowMilliamps, -691, 0xFD4D)]
    [InlineData(CurrentLoop.FaultHighMilliamps, 29376, 0x72C0)]
    public void Ne43FaultCurrents_In27648_KeepTheirValueWhenReadAsInt(double milliamps, int expectedCounts, int expectedRegister)
    {
        short counts = AnalogCodec.MilliampsToCounts(milliamps, AnalogFormat.Counts27648);
        ushort register = AnalogCodec.ToRegister(counts);

        // O que o mestre recebe são os dois bytes do registrador, em big-endian; lidos como INT, dão o mesmo valor.
        byte[] wire = [(byte)(register >> 8), (byte)(register & 0xFF)];
        short readAsInt = System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(wire);

        output.WriteLine($"{milliamps} mA -> {counts} contagens -> registrador 0x{register:X4} -> INT {readAsInt}");
        Assert.Equal(expectedCounts, counts);
        Assert.Equal(expectedRegister, register);
        Assert.Equal(expectedCounts, readAsInt);
        Assert.Equal(expectedCounts, AnalogCodec.FromRegister(register));
    }

    /// <summary>No formato 0–27648 a sobrefaixa vai até 32511 e a subfaixa até −4864 (representação Siemens).</summary>
    [Fact]
    public void Counts27648_ClampToSiemensOverAndUnderRange()
    {
        Assert.Equal(32511, AnalogCodec.MilliampsToCounts(30.0, AnalogFormat.Counts27648));
        Assert.Equal(-4864, AnalogCodec.MilliampsToCounts(0.0, AnalogFormat.Counts27648));
    }

    /// <summary>D28: nos outros formatos inteiros, o valor é limitado ao que cabe em <c>INT</c>.</summary>
    [Fact]
    public void OtherIntegerFormats_ClampToInt16()
    {
        Assert.Equal(32767, AnalogCodec.MilliampsToCounts(CurrentLoop.FaultHighMilliamps, AnalogFormat.Counts32767));
        Assert.Equal(-819, AnalogCodec.MilliampsToCounts(CurrentLoop.FaultLowMilliamps, AnalogFormat.Counts32767));
        Assert.Equal(10625, AnalogCodec.MilliampsToCounts(CurrentLoop.FaultHighMilliamps, AnalogFormat.Counts10000));
        Assert.Equal(-250, AnalogCodec.MilliampsToCounts(CurrentLoop.FaultLowMilliamps, AnalogFormat.Counts10000));
    }

    [Fact]
    public void Saturate_LimitsToNe43MeasurementRange()
    {
        Assert.Equal(3.8, CurrentLoop.Saturate(1.0));
        Assert.Equal(20.5, CurrentLoop.Saturate(25.0));
        Assert.Equal(12.0, CurrentLoop.Saturate(12.0));
    }

    /// <summary>
    /// Critério 3: <c>float32</c> com valores de referência fixos (padrões de bits IEEE 754 de precisão simples),
    /// nas duas ordens de palavra.
    /// </summary>
    [Theory]
    [InlineData(1.0f, 0x3F80, 0x0000)]
    [InlineData(-2.5f, 0xC020, 0x0000)]
    [InlineData(123.456f, 0x42F6, 0xE979)]
    [InlineData(0.6f, 0x3F19, 0x999A)]
    [InlineData(10.0f, 0x4120, 0x0000)]
    [InlineData(0.0f, 0x0000, 0x0000)]
    public void Float32_MatchesReferenceBitPatterns_InBothWordOrders(float value, int highWord, int lowWord)
    {
        Span<ushort> normal = stackalloc ushort[2];
        Span<ushort> swapped = stackalloc ushort[2];

        AnalogCodec.WriteFloat32(value, WordOrder.HighWordFirst, normal);
        AnalogCodec.WriteFloat32(value, WordOrder.LowWordFirst, swapped);

        output.WriteLine($"{value}: normal = {normal[0]:X4} {normal[1]:X4}; invertida = {swapped[0]:X4} {swapped[1]:X4}");
        Assert.Equal([(ushort)highWord, (ushort)lowWord], normal.ToArray());
        Assert.Equal([(ushort)lowWord, (ushort)highWord], swapped.ToArray());
        Assert.Equal(value, AnalogCodec.ReadFloat32(normal, WordOrder.HighWordFirst));
        Assert.Equal(value, AnalogCodec.ReadFloat32(swapped, WordOrder.LowWordFirst));
    }
}
