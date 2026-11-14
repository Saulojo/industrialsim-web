namespace HartPoc;

/// <summary>Quadro remontado do fluxo de bytes, com o resultado da verificação.</summary>
public readonly record struct ReceivedFrame(HartFrame Frame, bool CheckByteOk, int PreambleCount);

/// <summary>
/// Remonta quadros HART a partir de um fluxo de bytes que chega em pedaços de qualquer tamanho
/// (uma mensagem do WebSocket pode trazer meio quadro ou mais de um).
/// O início é achado por pelo menos dois preâmbulos seguidos de um delimitador válido; o fim,
/// pela contagem de bytes (HCF_SPEC-081 r9.0, 5.2). Lixo fora de quadro é descartado.
/// </summary>
public sealed class HartFrameReader
{
    /// <summary>Preâmbulos exigidos antes do delimitador (HCF_SPEC-127 r7.1, comando 0, byte 3).</summary>
    public const int MinimumPreambles = 2;

    private readonly List<byte> _body = [];
    private readonly long _gapTimeoutMs;
    private State _state = State.Hunting;
    private int _preambles;
    private int _framePreambles;
    private int _remaining;
    private int _addressLength;
    private int _expansionLength;
    private long _lastByteMs;

    /// <param name="gapTimeoutMs">
    /// Silêncio, dentro de um quadro, que faz desistir dele ("gap error", 5.2). Na linha real o limite
    /// é de um caractere (~9 ms a 1200 baud); aqui é folgado porque os bytes passam pelo navegador.
    /// </param>
    public HartFrameReader(long gapTimeoutMs = 300)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gapTimeoutMs);
        _gapTimeoutMs = gapTimeoutMs;
    }

    private enum State
    {
        Hunting,
        Address,
        Expansion,
        Command,
        ByteCount,
        Data,
        CheckByte,
    }

    /// <summary>Bytes jogados fora por não pertencerem a um quadro.</summary>
    public long DiscardedBytes { get; private set; }

    /// <summary>Quadros abandonados no meio por silêncio.</summary>
    public long AbortedFrames { get; private set; }

    /// <summary>Entrega bytes recebidos no instante <paramref name="nowMs"/> e devolve os quadros completados.</summary>
    public IReadOnlyList<ReceivedFrame> Feed(ReadOnlySpan<byte> bytes, long nowMs)
    {
        if (_state != State.Hunting && nowMs - _lastByteMs > _gapTimeoutMs)
        {
            AbortedFrames++;
            DiscardedBytes += _body.Count;
            Reset();
        }

        List<ReceivedFrame>? frames = null;
        foreach (var b in bytes)
        {
            if (Accept(b) is { } frame)
            {
                (frames ??= []).Add(frame);
            }
        }

        if (bytes.Length > 0)
        {
            _lastByteMs = nowMs;
        }

        return frames ?? (IReadOnlyList<ReceivedFrame>)[];
    }

    private static bool IsDelimiter(byte b)
    {
        // Bits 4–3 = camada física (0 = FSK assíncrona); bits 2–0 = tipo do quadro (1, 2 ou 6).
        var type = b & 0x07;
        return (b & 0x18) == 0 && type is (int)HartFrameType.Burst or (int)HartFrameType.Request or (int)HartFrameType.Response;
    }

    private ReceivedFrame? Accept(byte b)
    {
        switch (_state)
        {
            case State.Hunting:
                if (b == HartFrame.Preamble)
                {
                    _preambles++;
                }
                else if (_preambles >= MinimumPreambles && IsDelimiter(b))
                {
                    _framePreambles = _preambles;
                    _body.Clear();
                    _body.Add(b);
                    _addressLength = (b & 0x80) != 0 ? HartFrame.LongAddressLength : HartFrame.ShortAddressLength;
                    _expansionLength = (b >> 5) & 0x03;
                    _remaining = _addressLength;
                    _state = State.Address;
                }
                else
                {
                    DiscardedBytes += _preambles + 1;
                    _preambles = 0;
                }

                return null;

            case State.Address:
                _body.Add(b);
                if (--_remaining == 0)
                {
                    _remaining = _expansionLength;
                    _state = _expansionLength > 0 ? State.Expansion : State.Command;
                }

                return null;

            case State.Expansion:
                _body.Add(b);
                if (--_remaining == 0)
                {
                    _state = State.Command;
                }

                return null;

            case State.Command:
                _body.Add(b);
                _state = State.ByteCount;
                return null;

            case State.ByteCount:
                _body.Add(b);
                _remaining = b;
                _state = b > 0 ? State.Data : State.CheckByte;
                return null;

            case State.Data:
                _body.Add(b);
                if (--_remaining == 0)
                {
                    _state = State.CheckByte;
                }

                return null;

            default:
                _body.Add(b);
                var frame = Build();
                Reset();
                return frame;
        }
    }

    private ReceivedFrame Build()
    {
        var raw = _body.ToArray();
        var address = raw.AsSpan(1, _addressLength).ToArray();
        var expansion = raw.AsSpan(1 + _addressLength, _expansionLength).ToArray();
        var commandIndex = 1 + _addressLength + _expansionLength;
        var data = raw.AsSpan(commandIndex + 2, raw[commandIndex + 1]).ToArray();
        var frame = new HartFrame((HartFrameType)(raw[0] & 0x07), address, raw[commandIndex], data, expansion);

        // Com o byte de verificação incluído, o XOR de tudo tem de dar zero (5.4).
        return new ReceivedFrame(frame, HartFrame.ComputeCheckByte(raw) == 0, _framePreambles);
    }

    private void Reset()
    {
        _state = State.Hunting;
        _preambles = 0;
        _body.Clear();
    }
}
