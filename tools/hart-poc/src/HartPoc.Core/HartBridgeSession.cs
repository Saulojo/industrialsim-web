namespace HartPoc;

/// <summary>Contadores da ponte, compartilhados por todas as conexões.</summary>
public sealed class BridgeCounters
{
    private long _bytesReceived;
    private long _bytesSent;
    private long _framesReceived;
    private long _framesAnswered;
    private long _checkByteErrors;
    private long _discardedBytes;
    private long _abortedFrames;

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    public long FramesAnswered => Interlocked.Read(ref _framesAnswered);

    public long CheckByteErrors => Interlocked.Read(ref _checkByteErrors);

    public long DiscardedBytes => Interlocked.Read(ref _discardedBytes);

    public long AbortedFrames => Interlocked.Read(ref _abortedFrames);

    internal void AddReceived(int bytes) => Interlocked.Add(ref _bytesReceived, bytes);

    internal void AddSent(int bytes) => Interlocked.Add(ref _bytesSent, bytes);

    internal void AddFrame(bool checkByteOk, bool answered)
    {
        Interlocked.Increment(ref _framesReceived);
        if (!checkByteOk)
        {
            Interlocked.Increment(ref _checkByteErrors);
        }

        if (answered)
        {
            Interlocked.Increment(ref _framesAnswered);
        }
    }

    internal void AddDiscarded(long bytes, long frames)
    {
        Interlocked.Add(ref _discardedBytes, bytes);
        Interlocked.Add(ref _abortedFrames, frames);
    }
}

/// <summary>
/// Uma conexão da ponte: recebe os bytes crus vindos da serial (pelo WebSocket), remonta os quadros,
/// entrega ao instrumento e devolve os bytes da resposta. Cada linha do log traz os bytes em
/// hexadecimal, para virarem vetores de teste na Etapa 16.
/// </summary>
public sealed class HartBridgeSession
{
    private readonly HartFrameReader _reader = new();
    private readonly HartDevice _device;
    private readonly BridgeCounters _counters;
    private readonly Action<string> _log;
    private readonly int _responsePreambles;
    private long _discardedSeen;
    private long _abortedSeen;

    public HartBridgeSession(HartDevice device, BridgeCounters counters, Action<string> log, int responsePreambles = 5)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(log);
        _device = device;
        _counters = counters;
        _log = log;
        _responsePreambles = responsePreambles;
    }

    /// <summary>Processa um pedaço do fluxo; devolve os bytes a mandar de volta (vazio se não houver resposta).</summary>
    public byte[] OnBytes(ReadOnlySpan<byte> bytes, long nowMs)
    {
        _counters.AddReceived(bytes.Length);
        _log($"RX {Convert.ToHexString(bytes)}");

        var frames = _reader.Feed(bytes, nowMs);
        _counters.AddDiscarded(_reader.DiscardedBytes - _discardedSeen, _reader.AbortedFrames - _abortedSeen);
        _discardedSeen = _reader.DiscardedBytes;
        _abortedSeen = _reader.AbortedFrames;

        byte[] output = [];
        foreach (var received in frames)
        {
            var frame = received.Frame;
            var response = _device.HandleRequest(received);
            _counters.AddFrame(received.CheckByteOk, response is not null);
            _log(
                $"   pedido: comando {frame.Command}, endereço {Convert.ToHexString(frame.Address)}, dados {Convert.ToHexString(frame.Data)}, "
                + $"{received.PreambleCount} preâmbulos, verificação {(received.CheckByteOk ? "ok" : "ERRADA")}"
                + (response is null ? " -> sem resposta" : $" -> código de resposta {response.Data[0]}"));

            if (response is not null)
            {
                var encoded = response.Encode(_responsePreambles);
                _log($"TX {Convert.ToHexString(encoded)}");
                output = [.. output, .. encoded];
            }
        }

        _counters.AddSent(output.Length);
        return output;
    }
}
