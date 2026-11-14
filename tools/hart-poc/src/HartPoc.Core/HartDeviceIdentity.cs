namespace HartPoc;

/// <summary>
/// Identificação que o instrumento devolve no comando 0. O padrão imita, só para teste, o transmissor
/// de pressão Smar LD301: fabricante 0x3E (62) e tipo de dispositivo 0x3E01
/// (HCF_SPEC-183 r22.0, tabela 8 "Manufacturer Identification Codes" e tabela 1 "Device Type Codes").
/// </summary>
public sealed record HartDeviceIdentity
{
    /// <summary>Código do fabricante (tabela 8). 0x3E = Smar.</summary>
    public byte ManufacturerId { get; init; } = 0x3E;

    /// <summary>Tipo de dispositivo do fabricante. 0x01 = LD301 (tipo expandido 0x3E01).</summary>
    public byte DeviceType { get; init; } = 0x01;

    /// <summary>Número de série de 24 bits; valor arbitrário da prova.</summary>
    public uint DeviceId { get; init; } = 0x0E2E02;

    /// <summary>Revisão maior do protocolo: 5 (resposta curta do comando 0) ou 7 (resposta estendida).</summary>
    public int HartRevision { get; init; } = 5;

    // Revisões de dispositivo, software e hardware: valores assumidos, a ajustar ao que o DTM esperar.
    public byte DeviceRevision { get; init; } = 3;

    public byte SoftwareRevision { get; init; } = 1;

    public byte HardwareRevision { get; init; } = 1;

    /// <summary>Preâmbulos que o mestre deve mandar e que o instrumento manda na resposta.</summary>
    public byte Preambles { get; init; } = 5;

    /// <summary>Tipo expandido de 16 bits; no HART 5 é o fabricante seguido do tipo.</summary>
    public ushort ExpandedDeviceType => (ushort)((ManufacturerId << 8) | DeviceType);

    /// <summary>
    /// Endereço único de 5 bytes, sem os bits de mestre e burst: 14 bits baixos do tipo expandido
    /// e os 3 bytes do número de série (HCF_SPEC-081 r9.0, 5.3.2).
    /// </summary>
    public byte[] UniqueAddress() =>
    [
        (byte)(ManufacturerId & 0x3F),
        DeviceType,
        (byte)(DeviceId >> 16),
        (byte)(DeviceId >> 8),
        (byte)DeviceId,
    ];
}
