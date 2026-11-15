namespace HartPoc;

/// <summary>Tipo do quadro, nos 3 bits baixos do delimitador (HCF_SPEC-081 r9.0, 5.1.1).</summary>
public enum HartFrameType
{
    /// <summary>BACK: quadro de burst.</summary>
    Burst = 1,

    /// <summary>STX: do mestre para o instrumento.</summary>
    Request = 2,

    /// <summary>ACK: do instrumento para o mestre.</summary>
    Response = 6,
}

/// <summary>
/// Quadro HART (PDU): delimitador, endereço, bytes de expansão, comando, contagem de bytes,
/// dados e byte de verificação (HCF_SPEC-081 r9.0, 5.1). Os preâmbulos são da camada física
/// e só aparecem em <see cref="Encode"/>.
/// </summary>
public sealed class HartFrame
{
    public const byte Preamble = 0xFF;
    public const int ShortAddressLength = 1;
    public const int LongAddressLength = 5;

    private const byte MasterBit = 0x80;
    private const byte BurstBit = 0x40;

    public HartFrame(HartFrameType type, byte[] address, byte command, byte[] data, byte[]? expansionBytes = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(data);
        if (address.Length is not (ShortAddressLength or LongAddressLength))
        {
            throw new ArgumentException("O endereço tem 1 byte (polling) ou 5 bytes (único).", nameof(address));
        }

        if (data.Length > byte.MaxValue)
        {
            throw new ArgumentException("O campo de dados tem no máximo 255 bytes.", nameof(data));
        }

        expansionBytes ??= [];
        if (expansionBytes.Length > 3)
        {
            throw new ArgumentException("São no máximo 3 bytes de expansão.", nameof(expansionBytes));
        }

        Type = type;
        Address = address;
        Command = command;
        Data = data;
        ExpansionBytes = expansionBytes;
    }

    public HartFrameType Type { get; }

    public byte[] Address { get; }

    public byte[] ExpansionBytes { get; }

    public byte Command { get; }

    public byte[] Data { get; }

    public bool IsLongAddress => Address.Length == LongAddressLength;

    /// <summary>Bit mais alto do endereço: 1 = mestre primário, 0 = secundário (5.3.1).</summary>
    public bool FromPrimaryMaster => (Address[0] & MasterBit) != 0;

    public bool BurstMode => (Address[0] & BurstBit) != 0;

    /// <summary>Endereço de polling (6 bits) de um quadro de endereço curto (5.3.4).</summary>
    public byte PollingAddress => (byte)(Address[0] & 0x3F);

    /// <summary>Bit 7 = endereço único; bits 6–5 = nº de bytes de expansão; bits 2–0 = tipo.</summary>
    public byte Delimiter => (byte)((IsLongAddress ? 0x80 : 0x00) | (ExpansionBytes.Length << 5) | (int)Type);

    /// <summary>Pedido com endereço curto; só o comando 0 pode usá-lo (5.3.4).</summary>
    public static HartFrame ShortRequest(bool primaryMaster, byte pollingAddress, byte command, byte[]? data = null)
    {
        var address = (byte)((primaryMaster ? MasterBit : 0) | (pollingAddress & 0x3F));
        return new HartFrame(HartFrameType.Request, [address], command, data ?? []);
    }

    /// <summary>Pedido com endereço único; <paramref name="uniqueAddress"/> são os 5 bytes sem os bits de mestre e burst.</summary>
    public static HartFrame LongRequest(bool primaryMaster, ReadOnlySpan<byte> uniqueAddress, byte command, byte[]? data = null)
    {
        if (uniqueAddress.Length != LongAddressLength)
        {
            throw new ArgumentException("O endereço único tem 5 bytes.", nameof(uniqueAddress));
        }

        var address = uniqueAddress.ToArray();
        address[0] = (byte)((address[0] & 0x3F) | (primaryMaster ? MasterBit : 0));
        return new HartFrame(HartFrameType.Request, address, command, data ?? []);
    }

    /// <summary>Endereço de broadcast: 38 bits zerados no lugar do identificador único (5.3.3).</summary>
    public bool IsBroadcast
    {
        get
        {
            if (!IsLongAddress || (Address[0] & 0x3F) != 0)
            {
                return false;
            }

            return Address[1] == 0 && Address[2] == 0 && Address[3] == 0 && Address[4] == 0;
        }
    }

    /// <summary>XOR de todos os bytes do delimitador ao último dado (5.4).</summary>
    public static byte ComputeCheckByte(ReadOnlySpan<byte> bytes)
    {
        byte check = 0;
        foreach (var b in bytes)
        {
            check ^= b;
        }

        return check;
    }

    /// <summary>Bytes como vão na linha: preâmbulos, quadro e byte de verificação.</summary>
    public byte[] Encode(int preambleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(preambleCount);
        var body = 1 + Address.Length + ExpansionBytes.Length + 2 + Data.Length;
        var bytes = new byte[preambleCount + body + 1];
        bytes.AsSpan(0, preambleCount).Fill(Preamble);

        var i = preambleCount;
        bytes[i++] = Delimiter;
        Address.CopyTo(bytes, i);
        i += Address.Length;
        ExpansionBytes.CopyTo(bytes, i);
        i += ExpansionBytes.Length;
        bytes[i++] = Command;
        bytes[i++] = (byte)Data.Length;
        Data.CopyTo(bytes, i);
        i += Data.Length;
        bytes[i] = ComputeCheckByte(bytes.AsSpan(preambleCount, body));
        return bytes;
    }
}
