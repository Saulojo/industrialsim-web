using System.Buffers.Binary;

namespace HartPoc.Tests;

public class DeviceTests
{
    private static readonly byte[] UniqueAddress = [0x3E, 0x01, 0x0E, 0x2E, 0x02];

    private static HartDevice Device(int revision = 5, Func<double, double>? pv = null, List<string>? log = null) =>
        new(new HartDeviceIdentity { HartRevision = revision }, pv ?? (_ => 25.0), log is null ? null : log.Add);

    private static HartFrame? Send(HartDevice device, HartFrame request, bool checkByteOk = true) =>
        device.HandleRequest(new ReceivedFrame(request, checkByteOk, 5));

    private static HartFrame Long(HartDevice device, byte command, byte[]? data = null, bool primary = true)
    {
        var response = Send(device, HartFrame.LongRequest(primary, UniqueAddress, command, data));
        Assert.NotNull(response);
        return response;
    }

    private static float Float(HartFrame response, int offset) =>
        BinaryPrimitives.ReadSingleBigEndian(response.Data.AsSpan(2 + offset));

    private static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    [Fact]
    public void Command0_Hart5_IdentifiesAsSmarLd301()
    {
        var response = Send(Device(), HartFrame.ShortRequest(true, 0, 0));

        Assert.NotNull(response);
        Assert.Equal(HartFrameType.Response, response.Type);
        Assert.Equal([0x80], response.Address);
        Assert.Equal(0, response.Data[0]);
        Assert.Equal([254, 0x3E, 0x01, 5, 5, 3, 1, 0x08, 0, 0x0E, 0x2E, 0x02], response.Data[2..]);
    }

    [Fact]
    public void Command0_Hart7_HasExpandedDeviceTypeAnd22Bytes()
    {
        var response = Send(Device(revision: 7), HartFrame.ShortRequest(true, 0, 0));

        Assert.NotNull(response);
        var data = response.Data[2..];
        Assert.Equal(22, data.Length);
        Assert.Equal([254, 0x3E, 0x01], data[..3]);
        Assert.Equal(7, data[4]);
        Assert.Equal([0x00, 0x3E], data[17..19]);
    }

    [Fact]
    public void FirstResponse_HasColdStart_OnlyOncePerMaster()
    {
        var device = Device();

        Assert.Equal(0x20, Long(device, 1).Data[1] & 0x20);
        Assert.Equal(0x00, Long(device, 1).Data[1] & 0x20);
        Assert.Equal(0x20, Long(device, 1, primary: false).Data[1] & 0x20);
    }

    [Fact]
    public void ShortFrame_IsIgnored_ForOtherCommandsAndOtherPollingAddresses()
    {
        var device = Device();

        Assert.Null(Send(device, HartFrame.ShortRequest(true, 0, 1)));
        Assert.Null(Send(device, HartFrame.ShortRequest(true, 5, 0)));
    }

    [Fact]
    public void LongFrame_ForAnotherDevice_IsIgnored()
    {
        Assert.Null(Send(Device(), HartFrame.LongRequest(true, [0x3E, 0x01, 0x00, 0x00, 0x01], 1)));
    }

    [Fact]
    public void Response_EchoesTheMasterBit()
    {
        var device = Device();

        Assert.Equal(0xBE, Long(device, 1, primary: true).Address[0]);
        Assert.Equal(0x3E, Long(device, 1, primary: false).Address[0]);
    }

    [Fact]
    public void WrongCheckByte_AnswersOnlyTheCommunicationError()
    {
        var response = Send(Device(), HartFrame.LongRequest(true, UniqueAddress, 1), checkByteOk: false);

        Assert.NotNull(response);
        Assert.Equal([0x88, 0x00], response.Data);
    }

    [Fact]
    public void ExpansionBytes_AreNotAnswered()
    {
        var request = new HartFrame(HartFrameType.Request, [0x80], 0, [], expansionBytes: [0x01]);

        Assert.Null(Send(Device(), request));
    }

    [Fact]
    public void Command1And3_ReturnPvAndCurrent()
    {
        var device = Device(pv: _ => 25.0);

        var pv = Long(device, 1);
        Assert.Equal(PressureUnits.Kilopascal, pv.Data[2]);
        Assert.Equal(25f, Float(pv, 1));

        // 25 kPa em 0–50 kPa = 50% = 12 mA.
        var variables = Long(device, 3);
        Assert.Equal(11, variables.Data.Length);
        Assert.Equal(12f, Float(variables, 0));
        Assert.Equal(25f, Float(variables, 5));

        var loop = Long(device, 2);
        Assert.Equal(12f, Float(loop, 0));
        Assert.Equal(50f, Float(loop, 4));
    }

    [Fact]
    public void Command13_ReturnsTagDescriptorAndDate()
    {
        var response = Long(Device(), 13);

        Assert.Equal("PT-101  ", PackedAscii.Decode(response.Data.AsSpan(2, 6)));
        Assert.Equal("LD301 SIMULADO  ", PackedAscii.Decode(response.Data.AsSpan(8, 12)));
        Assert.Equal([14, 11, 126], response.Data[20..]);
    }

    [Fact]
    public void Command35_ChangesRange_Current_AndCommand15()
    {
        var log = new List<string>();
        var device = Device(pv: _ => 25.0, log: log);

        // Faixa nova de 0 a 0,25 bar (= 25 kPa): os mesmos 25 kPa passam a ser 100% = 20 mA.
        var written = Long(device, 35, [PressureUnits.Bar, .. Floats(0.25f, 0f)]);
        Assert.Equal(0, written.Data[0]);
        Assert.Equal(PressureUnits.Bar, written.Data[2]);
        Assert.Equal(0.25f, Float(written, 1));

        Assert.Equal(20f, Float(Long(device, 3), 0), 4);

        var information = Long(device, 15);
        Assert.Equal(19, information.Data.Length);
        Assert.Equal(PressureUnits.Bar, information.Data[4]);
        Assert.Equal(0.25f, Float(information, 3));
        Assert.Equal(0f, Float(information, 7));
        Assert.Contains(log, line => line.StartsWith("Comando 35", StringComparison.Ordinal));
    }

    [Fact]
    public void Command35_RejectsBadRequests()
    {
        var device = Device();

        Assert.Equal([5, 0x20], Long(device, 35, [PressureUnits.Kilopascal, 0, 0]).Data);
        Assert.Equal(18, Long(device, 35, [32, .. Floats(50f, 0f)]).Data[0]);
        Assert.Equal(11, Long(device, 35, [PressureUnits.Kilopascal, .. Floats(80f, 0f)]).Data[0]);
        Assert.Equal(10, Long(device, 35, [PressureUnits.Kilopascal, .. Floats(50f, -80f)]).Data[0]);
        Assert.Equal(29, Long(device, 35, [PressureUnits.Kilopascal, .. Floats(10.1f, 10f)]).Data[0]);
        Assert.Equal(50.0, device.Snapshot().UpperRangeKpa);
    }

    [Fact]
    public void Command34_Damping_Reaches63PercentOfTheStepInOneTimeConstant()
    {
        // Degrau de 0 para 10 kPa em t = 1 s; amortecimento de 2 s.
        var device = Device(pv: t => t < 1.0 ? 0.0 : 10.0);
        var written = Long(device, 34, Floats(2f));
        Assert.Equal(2f, Float(written, 0));

        for (var i = 0; i < 300; i++)
        {
            device.Advance(0.01);
        }

        // O degrau entra no passo que chega a t = 1 s, então em t = 3 s passou uma constante de tempo.
        var expected = 10.0 * (1.0 - Math.Exp(-1.0));
        Assert.Equal(expected, device.Snapshot().PvKpa, 0.05);
        Assert.Equal(2f, Float(Long(device, 15), 11));
    }

    [Fact]
    public void Command34_RejectsOutOfRangeValues()
    {
        var device = Device();

        Assert.Equal(4, Long(device, 34, Floats(-1f)).Data[0]);
        Assert.Equal(3, Long(device, 34, Floats(100f)).Data[0]);
        Assert.Equal(5, Long(device, 34, [0, 0]).Data[0]);
    }

    [Fact]
    public void ConfigurationChanged_IsSetByAWrite_AndClearedPerMasterByCommand38()
    {
        var device = Device();
        Assert.Equal(0x00, Long(device, 1).Data[1] & 0x40);

        Long(device, 34, Floats(1f));
        Assert.Equal(0x40, Long(device, 1).Data[1] & 0x40);

        Long(device, 38);
        Assert.Equal(0x00, Long(device, 1).Data[1] & 0x40);
        Assert.Equal(0x40, Long(device, 1, primary: false).Data[1] & 0x40);
    }

    [Fact]
    public void UnknownCommand_AnswersCommandNotImplemented()
    {
        var response = Long(Device(), 109);

        Assert.Equal(64, response.Data[0]);
        Assert.Equal(2, response.Data.Length);
    }

    [Fact]
    public void Command11_Broadcast_AnswersOnlyTheMatchingTag()
    {
        var device = Device();
        byte[] broadcast = [0, 0, 0, 0, 0];

        var found = Send(device, HartFrame.LongRequest(true, broadcast, 11, PackedAscii.Encode("PT-101", 8)));
        Assert.NotNull(found);
        Assert.Equal([0xBE, 0x01, 0x0E, 0x2E, 0x02], found.Address);
        Assert.Equal(254, found.Data[2]);

        Assert.Null(Send(device, HartFrame.LongRequest(true, broadcast, 11, PackedAscii.Encode("LT-101", 8))));
        Assert.Null(Send(device, HartFrame.LongRequest(true, broadcast, 1)));
    }

    [Fact]
    public void Current_SaturatesAndFlagsTheStatus_WhenPvLeavesTheRange()
    {
        var device = Device(pv: _ => 49.0);
        Long(device, 35, [PressureUnits.Kilopascal, .. Floats(20f, 0f)]);

        var response = Long(device, 3);

        Assert.Equal(20.5f, Float(response, 0));
        Assert.Equal(0x04, response.Data[1] & 0x04);
    }
}
