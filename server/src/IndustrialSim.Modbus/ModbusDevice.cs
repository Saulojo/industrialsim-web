using System.Buffers.Binary;
using IndustrialSim.IO;

namespace IndustrialSim.Modbus;

/// <summary>
/// O escravo Modbus de uma planta: atende as requisições (PDU) lendo e escrevendo direto na imagem de I/O, e
/// registra quem escreveu cada coil e holding register. Não faz rede: o <see cref="ModbusTcpServer"/> entrega
/// as requisições já separadas do TCP.
/// </summary>
/// <remarks>
/// A ordem das verificações segue o diagrama de estados do Modbus Application Protocol V1.1b3 (seção 6):
/// Unit ID (D37), função (exceção 01), quantidade e formato (03), endereço (02) e execução (04). Cada leitura
/// ou escrita é atômica na imagem: um passo da planta nunca vê metade de uma escrita múltipla.
/// Pode ser chamado por várias conexões ao mesmo tempo.
/// </remarks>
public sealed class ModbusDevice
{
    /// <summary>Máximo de bits numa leitura (funções 01 e 02).</summary>
    public const int MaxReadBits = 2000;

    /// <summary>Máximo de registradores numa leitura (funções 03 e 04).</summary>
    public const int MaxReadRegisters = 125;

    /// <summary>Máximo de coils numa escrita múltipla (função 15).</summary>
    public const int MaxWriteBits = 1968;

    /// <summary>Máximo de registradores numa escrita múltipla (função 16).</summary>
    public const int MaxWriteRegisters = 123;

    /// <summary>Unit ID padrão da planta (descritivo, mapa Modbus).</summary>
    public const byte DefaultUnitId = 1;

    private readonly TimeProvider _time;
    private int _unitId;
    private long _requests;
    private long _exceptionResponses;

    public ModbusDevice(IoImage image, byte unitId = DefaultUnitId, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        Image = image;
        UnitId = unitId;
        _time = time ?? TimeProvider.System;
        Writes = new WriteLog(image.CoilCount, image.HoldingRegisterCount);
    }

    public IoImage Image { get; }

    /// <summary>Autor e horário da última escrita em cada endereço.</summary>
    public WriteLog Writes { get; }

    /// <summary>
    /// Unit ID a que a planta responde; pode mudar com a planta rodando. Vale de 1 a 255 (0 é o endereço de
    /// difusão do Modbus serial, que não tem resposta).
    /// </summary>
    public byte UnitId
    {
        get => (byte)Volatile.Read(ref _unitId);
        set
        {
            ArgumentOutOfRangeException.ThrowIfZero(value, nameof(UnitId));
            Volatile.Write(ref _unitId, value);
        }
    }

    /// <summary>Requisições atendidas (com resposta normal ou de exceção).</summary>
    public long Requests => Interlocked.Read(ref _requests);

    /// <summary>Respostas de exceção enviadas.</summary>
    public long ExceptionResponses => Interlocked.Read(ref _exceptionResponses);

    /// <summary>
    /// Atende uma requisição e escreve a PDU da resposta em <paramref name="response"/>, que deve ter pelo menos
    /// <see cref="MbapHeader.MaxPduLength"/> bytes. Devolve o tamanho da resposta.
    /// </summary>
    /// <param name="unitId">Unit ID do cabeçalho MBAP.</param>
    /// <param name="request">PDU da requisição: código de função e dados.</param>
    /// <param name="response">Onde escrever a PDU da resposta.</param>
    /// <param name="author">Quem fez a requisição (IP do mestre), registrado nas escritas.</param>
    public int Process(byte unitId, ReadOnlySpan<byte> request, Span<byte> response, string author)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, MbapHeader.MaxPduLength, nameof(response));
        ArgumentOutOfRangeException.ThrowIfZero(request.Length, nameof(request));
        ArgumentException.ThrowIfNullOrEmpty(author);

        Interlocked.Increment(ref _requests);
        byte function = request[0];
        ModbusExceptionCode? error;
        int length = 0;
        if (unitId != UnitId)
        {
            error = ModbusExceptionCode.GatewayTargetDeviceFailedToRespond;
        }
        else
        {
            error = (ModbusFunction)function switch
            {
                ModbusFunction.ReadCoils => ReadBits(request, response, Image.TryReadCoils, out length),
                ModbusFunction.ReadDiscreteInputs => ReadBits(request, response, Image.TryReadDiscreteInputs, out length),
                ModbusFunction.ReadHoldingRegisters => ReadRegisters(request, response, Image.TryReadHoldingRegisters, out length),
                ModbusFunction.ReadInputRegisters => ReadRegisters(request, response, Image.TryReadInputRegisters, out length),
                ModbusFunction.WriteSingleCoil => WriteSingleCoil(request, response, author, out length),
                ModbusFunction.WriteSingleRegister => WriteSingleRegister(request, response, author, out length),
                ModbusFunction.WriteMultipleCoils => WriteMultipleCoils(request, response, author, out length),
                ModbusFunction.WriteMultipleRegisters => WriteMultipleRegisters(request, response, author, out length),
                _ => ModbusExceptionCode.IllegalFunction,
            };
        }

        if (error is ModbusExceptionCode code)
        {
            Interlocked.Increment(ref _exceptionResponses);
            response[0] = (byte)(function | 0x80);
            response[1] = (byte)code;
            return 2;
        }

        return length;
    }

    private delegate bool BitReader(int start, Span<bool> destination);

    private delegate bool RegisterReader(int start, Span<ushort> destination);

    private static ModbusExceptionCode? ReadBits(ReadOnlySpan<byte> request, Span<byte> response, BitReader read, out int length)
    {
        length = 0;
        if (request.Length != 5)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        int start = BinaryPrimitives.ReadUInt16BigEndian(request[1..]);
        int quantity = BinaryPrimitives.ReadUInt16BigEndian(request[3..]);
        if (quantity is < 1 or > MaxReadBits)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        Span<bool> bits = stackalloc bool[quantity];
        if (!read(start, bits))
        {
            return ModbusExceptionCode.IllegalDataAddress;
        }

        int byteCount = (quantity + 7) / 8;
        response[0] = request[0];
        response[1] = (byte)byteCount;
        Span<byte> packed = response.Slice(2, byteCount);
        packed.Clear();
        for (int i = 0; i < quantity; i++)
        {
            if (bits[i])
            {
                packed[i / 8] |= (byte)(1 << (i % 8));
            }
        }

        length = 2 + byteCount;
        return null;
    }

    private static ModbusExceptionCode? ReadRegisters(ReadOnlySpan<byte> request, Span<byte> response, RegisterReader read, out int length)
    {
        length = 0;
        if (request.Length != 5)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        int start = BinaryPrimitives.ReadUInt16BigEndian(request[1..]);
        int quantity = BinaryPrimitives.ReadUInt16BigEndian(request[3..]);
        if (quantity is < 1 or > MaxReadRegisters)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        Span<ushort> registers = stackalloc ushort[quantity];
        if (!read(start, registers))
        {
            return ModbusExceptionCode.IllegalDataAddress;
        }

        response[0] = request[0];
        response[1] = (byte)(2 * quantity);
        for (int i = 0; i < quantity; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response[(2 + (2 * i))..], registers[i]);
        }

        length = 2 + (2 * quantity);
        return null;
    }

    private ModbusExceptionCode? WriteSingleCoil(ReadOnlySpan<byte> request, Span<byte> response, string author, out int length)
    {
        length = 0;
        if (request.Length != 5)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        int address = BinaryPrimitives.ReadUInt16BigEndian(request[1..]);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(request[3..]);
        if (value is not (0xFF00 or 0x0000))
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        if (!Image.TryWriteCoil(address, value == 0xFF00))
        {
            return ModbusExceptionCode.IllegalDataAddress;
        }

        Writes.Record(ModbusArea.Coil, address, 1, author, _time.GetUtcNow());
        length = Echo(request, response);
        return null;
    }

    private ModbusExceptionCode? WriteSingleRegister(ReadOnlySpan<byte> request, Span<byte> response, string author, out int length)
    {
        length = 0;
        if (request.Length != 5)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        int address = BinaryPrimitives.ReadUInt16BigEndian(request[1..]);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(request[3..]);
        if (!Image.TryWriteHoldingRegister(address, value))
        {
            return ModbusExceptionCode.IllegalDataAddress;
        }

        Writes.Record(ModbusArea.HoldingRegister, address, 1, author, _time.GetUtcNow());
        length = Echo(request, response);
        return null;
    }

    private ModbusExceptionCode? WriteMultipleCoils(ReadOnlySpan<byte> request, Span<byte> response, string author, out int length)
    {
        length = 0;
        if (request.Length < 6)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        int start = BinaryPrimitives.ReadUInt16BigEndian(request[1..]);
        int quantity = BinaryPrimitives.ReadUInt16BigEndian(request[3..]);
        int byteCount = request[5];
        if (quantity is < 1 or > MaxWriteBits || byteCount != (quantity + 7) / 8 || request.Length != 6 + byteCount)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        Span<bool> bits = stackalloc bool[quantity];
        ReadOnlySpan<byte> packed = request.Slice(6, byteCount);
        for (int i = 0; i < quantity; i++)
        {
            bits[i] = (packed[i / 8] & (1 << (i % 8))) != 0;
        }

        if (!Image.TryWriteCoils(start, bits))
        {
            return ModbusExceptionCode.IllegalDataAddress;
        }

        Writes.Record(ModbusArea.Coil, start, quantity, author, _time.GetUtcNow());
        length = Echo(request, response);
        return null;
    }

    private ModbusExceptionCode? WriteMultipleRegisters(ReadOnlySpan<byte> request, Span<byte> response, string author, out int length)
    {
        length = 0;
        if (request.Length < 6)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        int start = BinaryPrimitives.ReadUInt16BigEndian(request[1..]);
        int quantity = BinaryPrimitives.ReadUInt16BigEndian(request[3..]);
        int byteCount = request[5];
        if (quantity is < 1 or > MaxWriteRegisters || byteCount != 2 * quantity || request.Length != 6 + byteCount)
        {
            return ModbusExceptionCode.IllegalDataValue;
        }

        Span<ushort> registers = stackalloc ushort[quantity];
        for (int i = 0; i < quantity; i++)
        {
            registers[i] = BinaryPrimitives.ReadUInt16BigEndian(request[(6 + (2 * i))..]);
        }

        if (!Image.TryWriteHoldingRegisters(start, registers))
        {
            return ModbusExceptionCode.IllegalDataAddress;
        }

        Writes.Record(ModbusArea.HoldingRegister, start, quantity, author, _time.GetUtcNow());
        length = Echo(request, response);
        return null;
    }

    /// <summary>
    /// Resposta das escritas: função, endereço e valor (escritas simples) ou quantidade (múltiplas), que são os
    /// 5 primeiros bytes da própria requisição.
    /// </summary>
    private static int Echo(ReadOnlySpan<byte> request, Span<byte> response)
    {
        request[..5].CopyTo(response);
        return 5;
    }
}
