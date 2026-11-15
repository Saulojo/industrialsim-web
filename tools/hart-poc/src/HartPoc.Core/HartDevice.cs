using System.Buffers.Binary;
using System.Globalization;

namespace HartPoc;

/// <summary>Retrato do instrumento para o log e para a página de estado.</summary>
public sealed record HartDeviceSnapshot(
    string Tag,
    double PvKpa,
    double LoopCurrentMa,
    double PercentOfRange,
    double LowerRangeKpa,
    double UpperRangeKpa,
    byte RangeUnitCode,
    double DampingS,
    int ConfigurationChangeCounter);

/// <summary>
/// Transmissor de pressão HART simulado (escravo), independente do transporte: recebe um quadro
/// de pedido e devolve o quadro de resposta, ou nada quando o pedido não é para ele.
/// Comandos: universais 0, 1, 2, 3, 11, 12, 13, 14, 15 e 16 (HCF_SPEC-127 r7.1) e de prática comum
/// 34, 35, 38 e 48 (HCF_SPEC-151 r10.0). Os demais respondem "comando não implementado".
/// </summary>
public sealed class HartDevice
{
    // Códigos de resposta (HCF_SPEC-307 r6.0 e tabelas de cada comando).
    private const byte Success = 0;
    private const byte ParameterTooLarge = 3;
    private const byte ParameterTooSmall = 4;
    private const byte TooFewDataBytes = 5;
    private const byte LowerRangeTooHigh = 9;
    private const byte LowerRangeTooLow = 10;
    private const byte UpperRangeTooHigh = 11;
    private const byte UpperRangeTooLow = 12;
    private const byte InvalidUnitsCode = 18;
    private const byte InvalidSpan = 29;
    private const byte CounterMismatch = 9;
    private const byte CommandNotImplemented = 64;

    // Erro de comunicação: bit 7 ligado; 0x08 = erro de paridade longitudinal (byte de verificação).
    private const byte CommunicationErrorCheckByte = 0x88;

    // Bits do status do dispositivo (segundo byte de status).
    private const byte StatusConfigurationChanged = 0x40;
    private const byte StatusColdStart = 0x20;
    private const byte StatusLoopCurrentSaturated = 0x04;
    private const byte StatusPvOutOfLimits = 0x01;

    // Limites do sensor: valores assumidos para um LD301 de faixa 2 (URL de 50 kPa e span mínimo de
    // URL/120); conferir no manual da Smar antes de usar fora desta prova.
    private const double UpperTransducerLimitKpa = 50.0;
    private const double LowerTransducerLimitKpa = -50.0;
    private const double MinimumSpanKpa = 50.0 / 120.0;
    private const double MaximumDampingS = 32.0;

    // Corrente de saída saturada nos limites de medição da NAMUR NE43 (3,8 e 20,5 mA).
    private const double MinimumCurrentMa = 3.8;
    private const double MaximumCurrentMa = 20.5;

    // Tabelas comuns (HCF_SPEC-183 r22.0): classificação 65 = pressão (tabela 21); unidades 39 = mA e
    // 57 = por cento; códigos de variável 244 a 246 (tabela 34); status 0xC0 = boa, 0x30 = ruim e constante.
    private const byte ClassificationPressure = 65;
    private const byte UnitsMilliamperes = 39;
    private const byte UnitsPercent = 57;
    private const byte VariablePercentOfRange = 244;
    private const byte VariableLoopCurrent = 245;
    private const byte VariablePrimary = 246;
    private const byte VariableStatusGood = 0xC0;
    private const byte VariableStatusBadConstant = 0x30;

    private const byte NotUsed = 250;
    private const byte None = 251;
    private const byte AlarmHigh = 0;
    private const byte TransferFunctionLinear = 0;

    private readonly Lock _gate = new();
    private readonly HartDeviceIdentity _identity;
    private readonly Func<double, double> _rawPvKpa;
    private readonly Action<string> _log;
    private readonly byte[] _uniqueAddress;

    private double _timeS;
    private double _pvKpa;
    private double _lowerRangeKpa;
    private double _upperRangeKpa = UpperTransducerLimitKpa;
    private byte _rangeUnitCode = PressureUnits.Kilopascal;
    private double _dampingS;
    private ushort _configurationChangeCounter;
    private bool _changedForPrimary;
    private bool _changedForSecondary;
    private bool _coldStartForPrimary = true;
    private bool _coldStartForSecondary = true;

    /// <param name="identity">Identificação devolvida no comando 0.</param>
    /// <param name="rawPvKpa">Pressão medida, em kPa, em função do tempo em segundos (antes do amortecimento).</param>
    /// <param name="log">Recebe uma linha por mudança de configuração.</param>
    public HartDevice(HartDeviceIdentity identity, Func<double, double> rawPvKpa, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(rawPvKpa);
        if (identity.HartRevision is not (5 or 7))
        {
            throw new ArgumentException("A prova só conhece as revisões 5 e 7 do HART.", nameof(identity));
        }

        _identity = identity;
        _rawPvKpa = rawPvKpa;
        _log = log ?? (_ => { });
        _uniqueAddress = identity.UniqueAddress();
        _pvKpa = rawPvKpa(0.0);
    }

    public byte PollingAddress { get; init; }

    public string Tag { get; init; } = "PT-101";

    public string LongTag { get; init; } = "PT-101 LD301 SIMULADO";

    public string Descriptor { get; init; } = "LD301 SIMULADO";

    public string Message { get; init; } = "INDUSTRIALSIM WEB - PROVA HART";

    /// <summary>Dia, mês e ano − 1900 (HCF_SPEC-127 r7.1, seção 5, "Date").</summary>
    public (byte Day, byte Month, byte YearSince1900) Date { get; init; } = (14, 11, 126);

    public uint FinalAssemblyNumber { get; init; } = 1;

    /// <summary>
    /// Avança o tempo do instrumento e aplica o amortecimento: filtro de 1ª ordem, discretizado de forma
    /// exata para entrada constante no passo (63,2% do degrau em uma constante de tempo;
    /// HCF_SPEC-151 r10.0, comando 34).
    /// </summary>
    public void Advance(double dtSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dtSeconds);
        lock (_gate)
        {
            _timeS += dtSeconds;
            var raw = _rawPvKpa(_timeS);
            _pvKpa = _dampingS <= 0.0 ? raw : _pvKpa + ((raw - _pvKpa) * (1.0 - Math.Exp(-dtSeconds / _dampingS)));
        }
    }

    public HartDeviceSnapshot Snapshot()
    {
        lock (_gate)
        {
            var (current, percent, _) = Output();
            return new HartDeviceSnapshot(
                Tag, _pvKpa, current, percent, _lowerRangeKpa, _upperRangeKpa, _rangeUnitCode, _dampingS, _configurationChangeCounter);
        }
    }

    /// <summary>Processa um pedido; devolve nulo quando o instrumento deve ficar calado.</summary>
    public HartFrame? HandleRequest(ReceivedFrame received)
    {
        var request = received.Frame;

        // Só pedidos (STX); bytes de expansão desconhecidos proíbem a resposta (HCF_SPEC-081, 5.1.3).
        if (request.Type != HartFrameType.Request || request.ExpansionBytes.Length > 0)
        {
            return null;
        }

        lock (_gate)
        {
            var broadcast = request.IsBroadcast;
            if (!request.IsLongAddress)
            {
                // Endereço curto: só o comando 0, e só no nosso endereço de polling (5.3.4).
                if (request.Command != 0 || request.PollingAddress != PollingAddress)
                {
                    return null;
                }
            }
            else if (!broadcast && !MatchesUniqueAddress(request.Address))
            {
                return null;
            }

            if (!received.CheckByteOk)
            {
                // Erro no byte de verificação: responde só com o erro de comunicação (tabela 1 de 5.4.1).
                return broadcast ? null : Reply(request, CommunicationErrorCheckByte, 0, []);
            }

            if (broadcast && request.Command != 11)
            {
                return null;
            }

            var primary = request.FromPrimaryMaster;
            var (responseCode, data) = Execute(request.Command, request.Data, primary);
            if (data is null)
            {
                return null;
            }

            var status = DeviceStatus(primary);
            if (primary)
            {
                _coldStartForPrimary = false;
            }
            else
            {
                _coldStartForSecondary = false;
            }

            return Reply(request, responseCode, status, data);
        }
    }

    private static float ReadFloat(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadSingleBigEndian(data);

    private static void WriteFloat(Span<byte> destination, double value) =>
        BinaryPrimitives.WriteSingleBigEndian(destination, (float)value);

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private bool MatchesUniqueAddress(byte[] address) =>
        (address[0] & 0x3F) == _uniqueAddress[0]
        && address[1] == _uniqueAddress[1]
        && address[2] == _uniqueAddress[2]
        && address[3] == _uniqueAddress[3]
        && address[4] == _uniqueAddress[4];

    private HartFrame Reply(HartFrame request, byte responseCode, byte deviceStatus, byte[] data)
    {
        // O bit de mestre volta igual; o de burst vai zerado (5.3.1). No broadcast, responde com o endereço próprio.
        byte[] address;
        if (request.IsBroadcast)
        {
            address = (byte[])_uniqueAddress.Clone();
            address[0] |= (byte)(request.Address[0] & 0x80);
        }
        else
        {
            address = (byte[])request.Address.Clone();
            address[0] &= 0xBF;
        }

        var payload = new byte[2 + data.Length];
        payload[0] = responseCode;
        payload[1] = deviceStatus;
        data.CopyTo(payload, 2);
        return new HartFrame(HartFrameType.Response, address, request.Command, payload);
    }

    private (double CurrentMa, double Percent, bool Saturated) Output()
    {
        var percent = (_pvKpa - _lowerRangeKpa) / (_upperRangeKpa - _lowerRangeKpa) * 100.0;
        var current = 4.0 + (0.16 * percent);
        var clamped = Math.Clamp(current, MinimumCurrentMa, MaximumCurrentMa);
        return (clamped, percent, clamped != current);
    }

    private byte DeviceStatus(bool primary)
    {
        byte status = 0;
        if (primary ? _changedForPrimary : _changedForSecondary)
        {
            status |= StatusConfigurationChanged;
        }

        if (primary ? _coldStartForPrimary : _coldStartForSecondary)
        {
            status |= StatusColdStart;
        }

        if (Output().Saturated)
        {
            status |= StatusLoopCurrentSaturated;
        }

        if (_pvKpa is > UpperTransducerLimitKpa or < LowerTransducerLimitKpa)
        {
            status |= StatusPvOutOfLimits;
        }

        return status;
    }

    private void MarkConfigurationChanged()
    {
        _changedForPrimary = true;
        _changedForSecondary = true;
        _configurationChangeCounter++;
    }

    // Dados nulos = não responder; resposta de erro leva só os dois bytes de status.
    private (byte ResponseCode, byte[]? Data) Execute(byte command, byte[] request, bool primary)
    {
        switch (command)
        {
            case 0:
                return (Success, Identity());

            case 1:
            {
                var data = new byte[5];
                data[0] = PressureUnits.Kilopascal;
                WriteFloat(data.AsSpan(1), _pvKpa);
                return (Success, data);
            }

            case 2:
            {
                var (current, percent, _) = Output();
                var data = new byte[8];
                WriteFloat(data, current);
                WriteFloat(data.AsSpan(4), percent);
                return (Success, data);
            }

            case 3:
            {
                // Só a PV: 9 bytes (HCF_SPEC-127, tabela 1 do comando 3).
                var data = new byte[9];
                WriteFloat(data, Output().CurrentMa);
                data[4] = PressureUnits.Kilopascal;
                WriteFloat(data.AsSpan(5), _pvKpa);
                return (Success, data);
            }

            case 7:
                // Endereço de polling e modo da corrente de malha (1 = habilitada; tabela 16).
                return (Success, [PollingAddress, 1]);

            case 8:
                // Classificação das variáveis dinâmicas (tabela 21): PV = pressão; as demais não existem.
                return (Success, [ClassificationPressure, NotUsed, NotUsed, NotUsed]);

            case 9:
                return ReadDeviceVariablesWithStatus(request);

            case 11:
                // Identificação pela tag: só quem tem a tag responde; os outros ficam calados.
                if (request.Length < 6)
                {
                    return (Success, null);
                }

                return PackedAscii.Decode(request.AsSpan(0, 6)).TrimEnd() == Tag.ToUpperInvariant()
                    ? (Success, Identity())
                    : (Success, null);

            case 12:
                return (Success, PackedAscii.Encode(Message, 32));

            case 13:
            {
                var data = new byte[21];
                PackedAscii.Encode(Tag, 8).CopyTo(data, 0);
                PackedAscii.Encode(Descriptor, 16).CopyTo(data, 6);
                data[18] = Date.Day;
                data[19] = Date.Month;
                data[20] = Date.YearSince1900;
                return (Success, data);
            }

            case 14:
            {
                var data = new byte[16];
                data[0] = (byte)(_identity.DeviceId >> 16);
                data[1] = (byte)(_identity.DeviceId >> 8);
                data[2] = (byte)_identity.DeviceId;
                data[3] = PressureUnits.Kilopascal;
                WriteFloat(data.AsSpan(4), UpperTransducerLimitKpa);
                WriteFloat(data.AsSpan(8), LowerTransducerLimitKpa);
                WriteFloat(data.AsSpan(12), MinimumSpanKpa);
                return (Success, data);
            }

            case 15:
                return (Success, DeviceInformation());

            case 16:
                return (Success, [(byte)(FinalAssemblyNumber >> 16), (byte)(FinalAssemblyNumber >> 8), (byte)FinalAssemblyNumber]);

            case 20:
            {
                // Tag longa: 32 caracteres Latin-1, completados com zeros.
                var data = new byte[32];
                System.Text.Encoding.Latin1.GetBytes(LongTag.AsSpan(0, Math.Min(32, LongTag.Length)), data);
                return (Success, data);
            }

            case 33:
                return ReadDeviceVariables(request);

            case 34:
                return WriteDamping(request);

            case 35:
                return WriteRange(request);

            case 38:
                return ResetConfigurationChanged(request, primary);

            case 48:
                // Sem status adicional: tudo zero (6 bytes no HART 5; 9 no HART 7).
                return (Success, new byte[_identity.HartRevision >= 7 ? 9 : 6]);

            case 50:
                // Variável de dispositivo de cada variável dinâmica: só a PV existe (variável 0).
                return (Success, [0, NotUsed, NotUsed, NotUsed]);

            default:
                return (CommandNotImplemented, []);
        }
    }

    // Valor, unidade e classificação de uma variável de dispositivo; os códigos 244 a 246 são os da
    // tabela 34 (percentual da faixa, corrente de malha e PV).
    private bool TryGetVariable(byte code, out byte units, out byte classification, out double value)
    {
        var (current, percent, _) = Output();
        switch (code)
        {
            case 0 or VariablePrimary:
                (units, classification, value) = (PressureUnits.Kilopascal, ClassificationPressure, _pvKpa);
                return true;
            case VariablePercentOfRange:
                (units, classification, value) = (UnitsPercent, 0, percent);
                return true;
            case VariableLoopCurrent:
                (units, classification, value) = (UnitsMilliamperes, 0, current);
                return true;
            default:
                (units, classification, value) = (NotUsed, 0, double.NaN);
                return false;
        }
    }

    private static void WriteNotANumber(Span<byte> destination)
    {
        // Valor de "variável não suportada": 7F A0 00 00 (HCF_SPEC-127 r7.1, comando 9).
        destination[0] = 0x7F;
        destination[1] = 0xA0;
        destination[2] = 0x00;
        destination[3] = 0x00;
    }

    private (byte ResponseCode, byte[]? Data) ReadDeviceVariablesWithStatus(byte[] request)
    {
        if (request.Length == 0)
        {
            return (TooFewDataBytes, []);
        }

        // Status estendido, 8 bytes por variável pedida (até 8) e o carimbo de tempo de 4 bytes.
        var slots = Math.Min(request.Length, 8);
        var data = new byte[1 + (slots * 8) + 4];
        for (var i = 0; i < slots; i++)
        {
            var slot = data.AsSpan(1 + (i * 8), 8);
            slot[0] = request[i];
            if (TryGetVariable(request[i], out var units, out var classification, out var value))
            {
                slot[1] = classification;
                slot[2] = units;
                WriteFloat(slot[3..], value);
                slot[7] = VariableStatusGood;
            }
            else
            {
                slot[2] = NotUsed;
                WriteNotANumber(slot[3..]);
                slot[7] = VariableStatusBadConstant;
            }
        }

        // Hora do dia em 1/32 de milissegundo, a partir do tempo do instrumento.
        var ticks = (uint)(_timeS % 86400.0 * 32000.0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1 + (slots * 8)), ticks);
        return (Success, data);
    }

    private (byte ResponseCode, byte[]? Data) ReadDeviceVariables(byte[] request)
    {
        if (request.Length == 0)
        {
            return (TooFewDataBytes, []);
        }

        // Código, unidade e valor de cada variável pedida, até 4 (HCF_SPEC-151 r10.0, comando 33).
        var slots = Math.Min(request.Length, 4);
        var data = new byte[slots * 6];
        for (var i = 0; i < slots; i++)
        {
            var slot = data.AsSpan(i * 6, 6);
            slot[0] = request[i];
            if (TryGetVariable(request[i], out var units, out _, out var value))
            {
                slot[1] = units;
                WriteFloat(slot[2..], value);
            }
            else
            {
                slot[1] = NotUsed;
                WriteNotANumber(slot[2..]);
            }
        }

        return (Success, data);
    }

    private byte[] Identity()
    {
        // Byte 7: revisão de hardware nos 5 bits altos; sinalização física 0 = Bell 202 em corrente.
        var hardware = (byte)(_identity.HardwareRevision << 3);
        if (_identity.HartRevision < 7)
        {
            // HART 5: 12 bytes, com fabricante e tipo de 1 byte cada.
            return
            [
                254, _identity.ManufacturerId, _identity.DeviceType, _identity.Preambles, 5,
                _identity.DeviceRevision, _identity.SoftwareRevision, hardware, 0,
                (byte)(_identity.DeviceId >> 16), (byte)(_identity.DeviceId >> 8), (byte)_identity.DeviceId,
            ];
        }

        // HART 7: 22 bytes (HCF_SPEC-127 r7.1, comando 0). Perfil 1 = dispositivo de processo.
        return
        [
            254, (byte)(_identity.ExpandedDeviceType >> 8), (byte)_identity.ExpandedDeviceType, _identity.Preambles, 7,
            _identity.DeviceRevision, _identity.SoftwareRevision, hardware, 0,
            (byte)(_identity.DeviceId >> 16), (byte)(_identity.DeviceId >> 8), (byte)_identity.DeviceId,
            _identity.Preambles, 0,
            (byte)(_configurationChangeCounter >> 8), (byte)_configurationChangeCounter,
            0,
            0, _identity.ManufacturerId,
            0, _identity.ManufacturerId,
            1,
        ];
    }

    private byte[] DeviceInformation()
    {
        PressureUnits.TryGetKpaPerUnit(_rangeUnitCode, out var kpaPerUnit);
        var hart7 = _identity.HartRevision >= 7;
        var data = new byte[hart7 ? 18 : 17];
        data[0] = AlarmHigh;
        data[1] = TransferFunctionLinear;
        data[2] = _rangeUnitCode;
        WriteFloat(data.AsSpan(3), _upperRangeKpa / kpaPerUnit);
        WriteFloat(data.AsSpan(7), _lowerRangeKpa / kpaPerUnit);
        WriteFloat(data.AsSpan(11), _dampingS);
        data[15] = None;

        // Byte 16: distribuidor no HART 5; reservado ("250") no HART 7, que acrescenta os flags do canal analógico.
        data[16] = hart7 ? NotUsed : _identity.ManufacturerId;
        return data;
    }

    private (byte ResponseCode, byte[]? Data) WriteDamping(byte[] request)
    {
        if (request.Length < 4)
        {
            return (TooFewDataBytes, []);
        }

        var damping = ReadFloat(request);
        if (float.IsNaN(damping) || damping < 0f)
        {
            return (ParameterTooSmall, []);
        }

        if (damping > MaximumDampingS)
        {
            return (ParameterTooLarge, []);
        }

        _log($"Comando 34: amortecimento de {Format(_dampingS)} s para {Format(damping)} s");
        _dampingS = damping;
        MarkConfigurationChanged();
        var data = new byte[4];
        WriteFloat(data, _dampingS);
        return (Success, data);
    }

    private (byte ResponseCode, byte[]? Data) WriteRange(byte[] request)
    {
        if (request.Length < 9)
        {
            return (TooFewDataBytes, []);
        }

        if (!PressureUnits.TryGetKpaPerUnit(request[0], out var kpaPerUnit))
        {
            return (InvalidUnitsCode, []);
        }

        var upper = ReadFloat(request.AsSpan(1)) * kpaPerUnit;
        var lower = ReadFloat(request.AsSpan(5)) * kpaPerUnit;
        if (double.IsNaN(upper) || double.IsNaN(lower))
        {
            return (InvalidSpan, []);
        }

        // Folga de arredondamento do float32 nas conversões de unidade.
        const double tolerance = 1e-4;
        if (upper > UpperTransducerLimitKpa + tolerance)
        {
            return (UpperRangeTooHigh, []);
        }

        if (upper < LowerTransducerLimitKpa - tolerance)
        {
            return (UpperRangeTooLow, []);
        }

        if (lower > UpperTransducerLimitKpa + tolerance)
        {
            return (LowerRangeTooHigh, []);
        }

        if (lower < LowerTransducerLimitKpa - tolerance)
        {
            return (LowerRangeTooLow, []);
        }

        if (Math.Abs(upper - lower) < MinimumSpanKpa - tolerance)
        {
            return (InvalidSpan, []);
        }

        _log(
            $"Comando 35: faixa de {Format(_lowerRangeKpa)}–{Format(_upperRangeKpa)} kPa para {Format(lower)}–{Format(upper)} kPa (unidade recebida: código {request[0]})");
        _upperRangeKpa = upper;
        _lowerRangeKpa = lower;
        _rangeUnitCode = request[0];
        MarkConfigurationChanged();

        // Devolve na mesma unidade recebida (HCF_SPEC-151 r10.0, comando 35).
        var data = new byte[9];
        data[0] = _rangeUnitCode;
        WriteFloat(data.AsSpan(1), _upperRangeKpa / kpaPerUnit);
        WriteFloat(data.AsSpan(5), _lowerRangeKpa / kpaPerUnit);
        return (Success, data);
    }

    private (byte ResponseCode, byte[]? Data) ResetConfigurationChanged(byte[] request, bool primary)
    {
        // HART 7: o mestre pode mandar o contador que leu; se não bater, a configuração mudou de novo.
        if (request.Length >= 2 && BinaryPrimitives.ReadUInt16BigEndian(request) != _configurationChangeCounter)
        {
            return (CounterMismatch, []);
        }

        if (primary)
        {
            _changedForPrimary = false;
        }
        else
        {
            _changedForSecondary = false;
        }

        _log($"Comando 38: bit de configuração alterada limpo para o mestre {(primary ? "primário" : "secundário")}");
        return _identity.HartRevision >= 7
            ? (Success, [(byte)(_configurationChangeCounter >> 8), (byte)_configurationChangeCounter])
            : (Success, []);
    }
}
