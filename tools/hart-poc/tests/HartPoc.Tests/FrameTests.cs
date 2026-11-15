namespace HartPoc.Tests;

public class FrameTests
{
    // Comando 0 em endereço curto, mestre primário, polling 0: 02 80 00 00, verificação 02^80 = 82.
    private static readonly byte[] Command0Short = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02, 0x80, 0x00, 0x00, 0x82];

    [Fact]
    public void Encode_ShortCommand0_MatchesKnownBytes()
    {
        var frame = HartFrame.ShortRequest(primaryMaster: true, pollingAddress: 0, command: 0);

        Assert.Equal(Command0Short, frame.Encode(preambleCount: 5));
    }

    [Fact]
    public void Encode_LongRequest_SetsDelimiterMasterBitAndCheckByte()
    {
        var frame = HartFrame.LongRequest(primaryMaster: true, [0x3E, 0x01, 0x0E, 0x2E, 0x02], command: 34, data: [0x40, 0x00, 0x00, 0x00]);

        var bytes = frame.Encode(preambleCount: 2);

        Assert.Equal([0xFF, 0xFF, 0x82, 0xBE, 0x01, 0x0E, 0x2E, 0x02, 0x22, 0x04, 0x40, 0x00, 0x00, 0x00], bytes[..^1]);
        Assert.Equal(0, HartFrame.ComputeCheckByte(bytes.AsSpan(2)));
    }

    [Fact]
    public void Reader_WholeFrame_IsDecoded()
    {
        var frames = new HartFrameReader().Feed(Command0Short, nowMs: 0);

        var received = Assert.Single(frames);
        Assert.True(received.CheckByteOk);
        Assert.Equal(5, received.PreambleCount);
        Assert.Equal(HartFrameType.Request, received.Frame.Type);
        Assert.False(received.Frame.IsLongAddress);
        Assert.True(received.Frame.FromPrimaryMaster);
        Assert.Equal(0, received.Frame.Command);
        Assert.Empty(received.Frame.Data);
    }

    [Fact]
    public void Reader_FrameSplitAtEveryPosition_GivesTheSameFrame()
    {
        var bytes = HartFrame.LongRequest(true, [0x3E, 0x01, 0x0E, 0x2E, 0x02], 35, [12, 1, 2, 3, 4, 5, 6, 7, 8]).Encode(5);

        for (var cut = 1; cut < bytes.Length; cut++)
        {
            var reader = new HartFrameReader();
            Assert.Empty(reader.Feed(bytes.AsSpan(0, cut), nowMs: 0));
            var received = Assert.Single(reader.Feed(bytes.AsSpan(cut), nowMs: 10));

            Assert.True(received.CheckByteOk);
            Assert.Equal(35, received.Frame.Command);
            Assert.Equal([12, 1, 2, 3, 4, 5, 6, 7, 8], received.Frame.Data);
        }
    }

    [Fact]
    public void Reader_TwoFramesInOneChunk_GivesBoth()
    {
        byte[] bytes = [.. Command0Short, .. HartFrame.ShortRequest(false, 3, 0).Encode(3)];

        var frames = new HartFrameReader().Feed(bytes, nowMs: 0);

        Assert.Equal(2, frames.Count);
        Assert.Equal(3, frames[1].Frame.PollingAddress);
        Assert.False(frames[1].Frame.FromPrimaryMaster);
    }

    [Fact]
    public void Reader_GarbageBeforeFrame_IsDiscarded()
    {
        var reader = new HartFrameReader();
        byte[] bytes = [0x00, 0x13, 0xFF, 0x55, 0x02, .. Command0Short];

        var received = Assert.Single(reader.Feed(bytes, nowMs: 0));

        Assert.True(received.CheckByteOk);
        Assert.Equal(5, reader.DiscardedBytes);
    }

    [Fact]
    public void Reader_SinglePreamble_DoesNotStartAFrame()
    {
        Assert.Empty(new HartFrameReader().Feed([0xFF, 0x02, 0x80, 0x00, 0x00, 0x82], nowMs: 0));
    }

    [Fact]
    public void Reader_WrongCheckByte_IsFlagged()
    {
        var bytes = (byte[])Command0Short.Clone();
        bytes[^1] ^= 0x01;

        var received = Assert.Single(new HartFrameReader().Feed(bytes, nowMs: 0));

        Assert.False(received.CheckByteOk);
    }

    [Fact]
    public void Reader_TruncatedFrame_IsDroppedAfterTheGapAndTheNextOneIsRead()
    {
        var reader = new HartFrameReader(gapTimeoutMs: 300);
        Assert.Empty(reader.Feed(Command0Short.AsSpan(0, 8), nowMs: 0));

        var received = Assert.Single(reader.Feed(Command0Short, nowMs: 1000));

        Assert.True(received.CheckByteOk);
        Assert.Equal(1, reader.AbortedFrames);
    }

    [Fact]
    public void Reader_ExpansionBytes_AreFramedSeparatelyFromData()
    {
        var frame = new HartFrame(HartFrameType.Request, [0x80], 0, [0xAA], expansionBytes: [0x11, 0x22]);

        var received = Assert.Single(new HartFrameReader().Feed(frame.Encode(5), nowMs: 0));

        Assert.True(received.CheckByteOk);
        Assert.Equal([0x11, 0x22], received.Frame.ExpansionBytes);
        Assert.Equal([0xAA], received.Frame.Data);
    }

    [Fact]
    public void PackedAscii_KnownVectorAndRoundTrip()
    {
        // A=01, B=02, C=03, D=04 em 6 bits: 000001 000010 000011 000100 = 04 20 C4.
        Assert.Equal([0x04, 0x20, 0xC4], PackedAscii.Encode("ABCD", 4));
        Assert.Equal("PT-101  ", PackedAscii.Decode(PackedAscii.Encode("pt-101", 8)));
        Assert.Equal(24, PackedAscii.Encode("MENSAGEM", 32).Length);
    }
}
