using IndustrialSim.IO;

namespace IndustrialSim.Modbus.Tests;

/// <summary>
/// As funções Modbus sobre a imagem de I/O, no nível da PDU (Modbus Application Protocol V1.1b3, seção 6):
/// formatos de requisição e resposta, exceções e registro de autor.
/// </summary>
public class ModbusDeviceTests
{
    private const string Author = "192.0.2.10";

    // Imagem pequena e conhecida: 10 coils, 12 entradas discretas, 4 holding registers e 6 input registers.
    private static (ModbusDevice Device, IoImage Image) Create(byte unitId = 1)
    {
        var image = new IoImage(coilCount: 10, discreteInputCount: 12, holdingRegisterCount: 4, inputRegisterCount: 6);
        return (new ModbusDevice(image, unitId), image);
    }

    private static byte[] Process(ModbusDevice device, params byte[] request) => ProcessForUnit(device, 1, request);

    private static byte[] ProcessForUnit(ModbusDevice device, byte unitId, params byte[] request)
    {
        var response = new byte[MbapHeader.MaxPduLength];
        int length = device.Process(unitId, request, response, Author);
        return response[..length];
    }

    [Fact]
    public void ReadHoldingRegisters_ReturnsBigEndianValues()
    {
        var (device, image) = Create();
        Assert.True(image.TryWriteHoldingRegisters(1, [0x1234, 0xABCD]));

        byte[] response = Process(device, 0x03, 0x00, 0x01, 0x00, 0x02);

        Assert.Equal([0x03, 0x04, 0x12, 0x34, 0xAB, 0xCD], response);
    }

    [Fact]
    public void ReadInputRegisters_ReadsTheInputArea()
    {
        var (device, image) = Create();
        image.Publish([], [1, 2, 3, 4, 5, 0xFFFF], [], [], AnalogFormat.Counts27648, WordOrder.HighWordFirst, 1, false);

        byte[] response = Process(device, 0x04, 0x00, 0x04, 0x00, 0x02);

        Assert.Equal([0x04, 0x04, 0x00, 0x05, 0xFF, 0xFF], response);
    }

    /// <summary>Bits empacotados do menos para o mais significativo, com o último byte completado com zeros.</summary>
    [Fact]
    public void ReadCoilsAndDiscreteInputs_PackBitsLsbFirst()
    {
        var (device, image) = Create();
        Assert.True(image.TryWriteCoils(0, [true, false, true, true, false, false, false, false, true, true]));
        image.Publish(
            [false, true, false, false, false, false, false, false, false, false, false, true],
            new ushort[6], [], [], AnalogFormat.Counts27648, WordOrder.HighWordFirst, 1, false);

        Assert.Equal([0x01, 0x02, 0x0D, 0x03], Process(device, 0x01, 0x00, 0x00, 0x00, 0x0A));
        Assert.Equal([0x02, 0x02, 0x02, 0x08], Process(device, 0x02, 0x00, 0x00, 0x00, 0x0C));
        Assert.Equal([0x01, 0x01, 0x03], Process(device, 0x01, 0x00, 0x08, 0x00, 0x02));
    }

    [Fact]
    public void WriteSingleCoil_EchoesTheRequest_AndAcceptsOnlyFF00Or0000()
    {
        var (device, image) = Create();

        Assert.Equal([0x05, 0x00, 0x03, 0xFF, 0x00], Process(device, 0x05, 0x00, 0x03, 0xFF, 0x00));
        Assert.True(image.Snapshot().Coils[3]);
        Assert.Equal([0x05, 0x00, 0x03, 0x00, 0x00], Process(device, 0x05, 0x00, 0x03, 0x00, 0x00));
        Assert.False(image.Snapshot().Coils[3]);

        Assert.Equal([0x85, 0x03], Process(device, 0x05, 0x00, 0x03, 0x00, 0x01));
    }

    [Fact]
    public void WriteSingleRegister_EchoesTheRequest()
    {
        var (device, image) = Create();

        Assert.Equal([0x06, 0x00, 0x02, 0x6C, 0x00], Process(device, 0x06, 0x00, 0x02, 0x6C, 0x00));

        Assert.Equal(27648, image.Snapshot().HoldingRegisters[2]);
    }

    [Fact]
    public void WriteMultipleCoils_UnpacksBits_AndAnswersStartAndQuantity()
    {
        var (device, image) = Create();

        byte[] response = Process(device, 0x0F, 0x00, 0x01, 0x00, 0x09, 0x02, 0b1000_0101, 0b0000_0001);

        Assert.Equal([0x0F, 0x00, 0x01, 0x00, 0x09], response);
        Assert.Equal(
            [false, true, false, true, false, false, false, false, true, true],
            image.Snapshot().Coils.ToArray());
    }

    [Fact]
    public void WriteMultipleRegisters_WritesAllAtOnce_AndAnswersStartAndQuantity()
    {
        var (device, image) = Create();

        byte[] response = Process(device, 0x10, 0x00, 0x00, 0x00, 0x02, 0x04, 0x00, 0x0A, 0x01, 0x02);

        Assert.Equal([0x10, 0x00, 0x00, 0x00, 0x02], response);
        Assert.Equal([10, 258, 0, 0], image.Snapshot().HoldingRegisters.ToArray());
    }

    /// <summary>Critério 4 (no nível da PDU): endereço fora do mapa devolve a exceção 02, sem mudar a imagem.</summary>
    [Theory]
    [InlineData(new byte[] { 0x01, 0x00, 0x09, 0x00, 0x02 }, 0x81)]
    [InlineData(new byte[] { 0x02, 0x00, 0x0C, 0x00, 0x01 }, 0x82)]
    [InlineData(new byte[] { 0x03, 0x00, 0x03, 0x00, 0x02 }, 0x83)]
    [InlineData(new byte[] { 0x04, 0x00, 0xC8, 0x00, 0x01 }, 0x84)]
    [InlineData(new byte[] { 0x05, 0x00, 0x0A, 0xFF, 0x00 }, 0x85)]
    [InlineData(new byte[] { 0x06, 0xFF, 0xFF, 0x00, 0x01 }, 0x86)]
    [InlineData(new byte[] { 0x0F, 0x00, 0x09, 0x00, 0x02, 0x01, 0x03 }, 0x8F)]
    [InlineData(new byte[] { 0x10, 0x00, 0x03, 0x00, 0x02, 0x04, 0x00, 0x01, 0x00, 0x02 }, 0x90)]
    public void AddressOutsideTheMap_ReturnsException02(byte[] request, byte function)
    {
        var (device, image) = Create();
        IoSnapshot before = image.Snapshot();

        Assert.Equal([function, 0x02], Process(device, request));

        IoSnapshot after = image.Snapshot();
        Assert.Equal(before.Coils.ToArray(), after.Coils.ToArray());
        Assert.Equal(before.HoldingRegisters.ToArray(), after.HoldingRegisters.ToArray());
        Assert.Empty(device.Writes.Entries());
        Assert.Equal(1, device.ExceptionResponses);
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 })] // quantidade 0
    [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x07, 0xD1 })] // 2001 bits
    [InlineData(new byte[] { 0x03, 0x00, 0x00, 0x00, 0x7E })] // 126 registradores
    [InlineData(new byte[] { 0x04, 0x00, 0x00, 0x00 })] // PDU curta
    [InlineData(new byte[] { 0x06, 0x00, 0x00, 0x00, 0x01, 0x00 })] // PDU longa
    [InlineData(new byte[] { 0x0F, 0x00, 0x00, 0x00, 0x09, 0x01, 0xFF })] // contagem de bytes não confere
    [InlineData(new byte[] { 0x10, 0x00, 0x00, 0x00, 0x7C, 0x00 })] // 124 registradores
    [InlineData(new byte[] { 0x10, 0x00, 0x00, 0x00, 0x01, 0x02, 0x00 })] // faltam dados
    public void BadQuantityOrLength_ReturnsException03(byte[] request)
    {
        var (device, _) = Create();

        Assert.Equal([(byte)(request[0] | 0x80), 0x03], Process(device, request));
    }

    [Theory]
    [InlineData(0x07)] // Read Exception Status (só serial)
    [InlineData(0x08)] // Diagnostics (só serial)
    [InlineData(0x17)] // Read/Write Multiple Registers
    [InlineData(0x2B)] // Encapsulated Interface Transport
    public void UnsupportedFunction_ReturnsException01(byte function)
    {
        var (device, _) = Create();

        Assert.Equal([(byte)(function | 0x80), 0x01], Process(device, function, 0x00, 0x00, 0x00, 0x01));
    }

    /// <summary>D37: outro Unit ID recebe a exceção 0x0B, e nada é lido ou escrito.</summary>
    [Fact]
    public void OtherUnitId_ReturnsException0B_AndChangingTheUnitIdTakesEffectImmediately()
    {
        var (device, image) = Create(unitId: 1);

        Assert.Equal([0x86, 0x0B], ProcessForUnit(device, 255, 0x06, 0x00, 0x00, 0x00, 0x07));
        Assert.Equal(0, image.Snapshot().HoldingRegisters[0]);

        device.UnitId = 255;

        Assert.Equal([0x06, 0x00, 0x00, 0x00, 0x07], ProcessForUnit(device, 255, 0x06, 0x00, 0x00, 0x00, 0x07));
        Assert.Equal([0x83, 0x0B], ProcessForUnit(device, 1, 0x03, 0x00, 0x00, 0x00, 0x01));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.UnitId = 0);
    }

    [Fact]
    public void Writes_RecordTheAuthorAndTime_PerAddress()
    {
        var time = new ManualTimeProvider();
        var image = new IoImage(10, 0, 4, 0);
        var device = new ModbusDevice(image, time: time);
        var response = new byte[MbapHeader.MaxPduLength];

        device.Process(1, [0x10, 0x00, 0x01, 0x00, 0x02, 0x04, 0x00, 0x01, 0x00, 0x02], response, "10.0.0.1");
        DateTimeOffset first = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(3));
        device.Process(1, [0x06, 0x00, 0x02, 0x00, 0x09], response, "10.0.0.2");
        device.Process(1, [0x05, 0x00, 0x04, 0xFF, 0x00], response, "10.0.0.2");
        device.Process(1, [0x03, 0x00, 0x00, 0x00, 0x04], response, "10.0.0.3");

        Assert.Equal(
            [
                new WriteEntry(ModbusArea.Coil, 4, new WriteRecord("10.0.0.2", first.AddSeconds(3))),
                new WriteEntry(ModbusArea.HoldingRegister, 1, new WriteRecord("10.0.0.1", first)),
                new WriteEntry(ModbusArea.HoldingRegister, 2, new WriteRecord("10.0.0.2", first.AddSeconds(3))),
            ],
            device.Writes.Entries());
        Assert.Null(device.Writes.Get(ModbusArea.HoldingRegister, 0));
        Assert.Equal(4, device.Requests);
        Assert.Equal(0, device.ExceptionResponses);
    }

    [Fact]
    public void MbapHeader_RoundTrips_AndRejectsOtherProtocolsAndLengths()
    {
        var bytes = new byte[MbapHeader.Size];
        MbapHeader.WriteResponse(bytes, transactionId: 0x0102, unitId: 7, pduLength: 5);

        Assert.Equal([0x01, 0x02, 0x00, 0x00, 0x00, 0x06, 0x07], bytes);
        MbapHeader header = MbapHeader.Read(bytes);
        Assert.Equal(new MbapHeader(0x0102, 0, 6, 7), header);
        Assert.True(header.IsValid);
        Assert.Equal(5, header.PduLength);

        Assert.False(new MbapHeader(1, 1, 6, 1).IsValid); // outro protocolo
        Assert.False(new MbapHeader(1, 0, 1, 1).IsValid); // sem código de função
        Assert.True(new MbapHeader(1, 0, 254, 1).IsValid); // PDU de 253 bytes
        Assert.False(new MbapHeader(1, 0, 255, 1).IsValid);
    }
}
