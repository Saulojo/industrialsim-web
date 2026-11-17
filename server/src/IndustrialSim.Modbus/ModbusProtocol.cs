using System.Buffers.Binary;

namespace IndustrialSim.Modbus;

/// <summary>Códigos de função atendidos pelo servidor (Modbus Application Protocol V1.1b3, seção 6).</summary>
public enum ModbusFunction : byte
{
    ReadCoils = 0x01,
    ReadDiscreteInputs = 0x02,
    ReadHoldingRegisters = 0x03,
    ReadInputRegisters = 0x04,
    WriteSingleCoil = 0x05,
    WriteSingleRegister = 0x06,
    WriteMultipleCoils = 0x0F,
    WriteMultipleRegisters = 0x10,
}

/// <summary>Códigos de exceção (Modbus Application Protocol V1.1b3, seção 7).</summary>
public enum ModbusExceptionCode : byte
{
    /// <summary>Função não suportada.</summary>
    IllegalFunction = 0x01,

    /// <summary>Endereço fora do mapa da planta.</summary>
    IllegalDataAddress = 0x02,

    /// <summary>Quantidade ou valor inválido, ou requisição mal formada.</summary>
    IllegalDataValue = 0x03,

    /// <summary>Falha do servidor ao executar a requisição.</summary>
    ServerDeviceFailure = 0x04,

    /// <summary>Unit ID diferente do configurado na planta (D37).</summary>
    GatewayTargetDeviceFailedToRespond = 0x0B,
}

/// <summary>
/// Cabeçalho MBAP do Modbus TCP (Modbus Messaging on TCP/IP Implementation Guide V1.0b, seção 3.1.3):
/// transação (2 bytes), protocolo (2 bytes, sempre 0), comprimento (2 bytes: Unit ID mais PDU) e Unit ID.
/// Todos os campos em big-endian.
/// </summary>
/// <param name="TransactionId">Identificador da transação, devolvido igual na resposta.</param>
/// <param name="ProtocolId">0 para Modbus.</param>
/// <param name="Length">Bytes que seguem o campo de comprimento: Unit ID mais PDU.</param>
/// <param name="UnitId">Unit ID.</param>
public readonly record struct MbapHeader(ushort TransactionId, ushort ProtocolId, ushort Length, byte UnitId)
{
    /// <summary>Tamanho do cabeçalho em bytes.</summary>
    public const int Size = 7;

    /// <summary>Maior PDU do Modbus: 253 bytes (ADU serial de 256 menos endereço e CRC).</summary>
    public const int MaxPduLength = 253;

    /// <summary>Maior ADU do Modbus TCP: cabeçalho mais PDU (260 bytes).</summary>
    public const int MaxAduLength = Size + MaxPduLength;

    /// <summary>Bytes de PDU que seguem o cabeçalho.</summary>
    public int PduLength => Length - 1;

    /// <summary>
    /// Se o cabeçalho descreve um quadro Modbus válido: protocolo 0 e PDU de 1 a 253 bytes. Um cabeçalho
    /// inválido faz o servidor fechar a conexão, porque o fluxo TCP perdeu o enquadramento.
    /// </summary>
    public bool IsValid => ProtocolId == 0 && PduLength >= 1 && PduLength <= MaxPduLength;

    public static MbapHeader Read(ReadOnlySpan<byte> source) => new(
        BinaryPrimitives.ReadUInt16BigEndian(source),
        BinaryPrimitives.ReadUInt16BigEndian(source[2..]),
        BinaryPrimitives.ReadUInt16BigEndian(source[4..]),
        source[6]);

    /// <summary>Escreve o cabeçalho de uma resposta com <paramref name="pduLength"/> bytes de PDU.</summary>
    public static void WriteResponse(Span<byte> destination, ushort transactionId, byte unitId, int pduLength)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..], (ushort)(pduLength + 1));
        destination[6] = unitId;
    }
}
