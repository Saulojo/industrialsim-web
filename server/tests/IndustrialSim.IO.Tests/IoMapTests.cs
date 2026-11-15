using IndustrialSim.Core.Scenarios;

namespace IndustrialSim.IO.Tests;

/// <summary>Etapa 4: o mapa da caldeira é o do descritivo, com endereços em base 0 e na notação clássica.</summary>
public class IoMapTests(ITestOutputHelper output)
{
    [Fact]
    public void CaldeiraMap_MatchesTheProjectDescription_InBothNotations()
    {
        IoMap map = Caldeira.Create().Map;

        IReadOnlyList<IoMapEntry> entries = map.Describe(AnalogFormat.Counts27648);
        foreach (IoMapEntry entry in entries)
        {
            output.WriteLine($"{entry.Area,-16} {entry.Address,4} {entry.ClassicAddress,6} x{entry.Length} {entry.Signal} {entry.Unit}");
        }

        (ModbusArea, int, int, int, string)[] expected =
        [
            (ModbusArea.Coil, 0, 1, 1, "P-101_run"),
            (ModbusArea.DiscreteInput, 0, 10001, 1, "P-101_running"),
            (ModbusArea.DiscreteInput, 1, 10002, 1, "LSLL-101"),
            (ModbusArea.DiscreteInput, 2, 10003, 1, "LSHH-101"),
            (ModbusArea.DiscreteInput, 3, 10004, 1, "PSH-101"),
            (ModbusArea.DiscreteInput, 4, 10005, 1, "PSV-101_open"),
            (ModbusArea.DiscreteInput, 5, 10006, 1, "watchdog"),
            (ModbusArea.HoldingRegister, 0, 40001, 1, "FV-101_command"),
            (ModbusArea.HoldingRegister, 1, 40002, 1, "FV-102_command"),
            (ModbusArea.HoldingRegister, 10, 40011, 1, "heartbeat"),
            (ModbusArea.InputRegister, 0, 30001, 1, "LT-101"),
            (ModbusArea.InputRegister, 1, 30002, 1, "PT-101"),
            (ModbusArea.InputRegister, 2, 30003, 1, "FT-101"),
            (ModbusArea.InputRegister, 3, 30004, 1, "FT-102"),
            (ModbusArea.InputRegister, 4, 30005, 1, "FV-101_position"),
            (ModbusArea.InputRegister, 5, 30006, 1, "FV-102_position"),
            (ModbusArea.InputRegister, 100, 30101, 2, "LT-101"),
            (ModbusArea.InputRegister, 102, 30103, 2, "PT-101"),
            (ModbusArea.InputRegister, 104, 30105, 2, "FT-101"),
            (ModbusArea.InputRegister, 106, 30107, 2, "FT-102"),
            (ModbusArea.InputRegister, 108, 30109, 2, "FV-103_position"),
        ];

        Assert.Equal(expected, entries.Select(e => (e.Area, e.Address, e.ClassicAddress, e.Length, e.Signal)));
        Assert.Equal((1, 6, 11, 110), (map.CoilCount, map.DiscreteInputCount, map.HoldingRegisterCount, map.InputRegisterCount));
    }

    /// <summary>D27: no <c>float32</c>, o sinal de endereço a ocupa 2a e 2a+1; heartbeat e diagnóstico não mudam.</summary>
    [Fact]
    public void Float32Layout_DoublesAnalogAddresses_AndKeepsHeartbeatAndDiagnostics()
    {
        IReadOnlyList<IoMapEntry> entries = Caldeira.Create().Map.Describe(AnalogFormat.Float32);

        Assert.Equal(
            [(0, 2), (2, 2), (10, 1)],
            entries.Where(e => e.Area == ModbusArea.HoldingRegister).Select(e => (e.Address, e.Length)));
        Assert.Equal(
            [(0, 2), (2, 2), (4, 2), (6, 2), (8, 2), (10, 2), (100, 2), (102, 2), (104, 2), (106, 2), (108, 2)],
            entries.Where(e => e.Area == ModbusArea.InputRegister).Select(e => (e.Address, e.Length)));
    }

    [Theory]
    [InlineData(ModbusArea.Coil, 0, 1)]
    [InlineData(ModbusArea.DiscreteInput, 0, 10001)]
    [InlineData(ModbusArea.InputRegister, 0, 30001)]
    [InlineData(ModbusArea.HoldingRegister, 0, 40001)]
    [InlineData(ModbusArea.HoldingRegister, 9998, 49999)]
    [InlineData(ModbusArea.HoldingRegister, 9999, 410000)]
    [InlineData(ModbusArea.InputRegister, 65535, 365536)]
    public void ClassicNotation_AddsTheAreaPrefix(ModbusArea area, int address, int expected) =>
        Assert.Equal(expected, ModbusAddress.Classic(area, address));

    [Fact]
    public void ScenarioWithoutIoSection_IsRefusedWithAClearMessage()
    {
        const string Json = """
            { "schemaVersion": 1, "id": "sem-io", "components": [ { "id": "LT-1", "type": "transmitter" } ] }
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => new PlantIo(ScenarioPlant.FromJson(Json)));

        Assert.Contains("'sem-io'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'io'", exception.Message, StringComparison.Ordinal);
    }
}
