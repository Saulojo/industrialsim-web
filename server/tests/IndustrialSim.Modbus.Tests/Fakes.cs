using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace IndustrialSim.Modbus.Tests;

/// <summary>Relógio controlado pelo teste: o tempo só anda por <see cref="Advance"/>.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _utcNow = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public void Advance(TimeSpan duration)
    {
        lock (_gate)
        {
            _utcNow += duration;
        }
    }
}

/// <summary>Como o pareamento de teste responde.</summary>
internal enum GateMode
{
    Allow,
    Refuse,
    Hold,
}

/// <summary>
/// Pareamento de teste: autoriza, recusa ou segura cada conexão até o teste decidir, e conta as conexões
/// autorizadas ainda abertas.
/// </summary>
internal sealed class TestGate(GateMode mode = GateMode.Allow) : IConnectionGate
{
    private readonly Channel<HeldRequest> _held = Channel.CreateUnbounded<HeldRequest>();
    private int _active;

    public int ActiveLeases => Volatile.Read(ref _active);

    public List<IPAddress> Addresses { get; } = [];

    public Task<IDisposable?> AuthorizeAsync(IPAddress remoteAddress, CancellationToken cancellationToken)
    {
        lock (Addresses)
        {
            Addresses.Add(remoteAddress);
        }

        switch (mode)
        {
            case GateMode.Allow:
                return Task.FromResult<IDisposable?>(NewLease());
            case GateMode.Refuse:
                return Task.FromResult<IDisposable?>(null);
            default:
                var request = new HeldRequest(remoteAddress, this);
                _held.Writer.TryWrite(request);
                return request.Decision.Task;
        }
    }

    /// <summary>Próxima conexão segurada pelo pareamento.</summary>
    public async Task<HeldRequest> NextHeldAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await _held.Reader.ReadAsync(timeout.Token);
    }

    private Lease NewLease()
    {
        Interlocked.Increment(ref _active);
        return new Lease(this);
    }

    internal sealed class HeldRequest(IPAddress address, TestGate gate)
    {
        public IPAddress Address { get; } = address;

        public TaskCompletionSource<IDisposable?> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Allow() => Decision.SetResult(gate.NewLease());

        public void Refuse() => Decision.SetResult(null);
    }

    private sealed class Lease(TestGate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref gate._active);
            }
        }
    }
}

/// <summary>
/// Mestre Modbus TCP mínimo, de socket, para os testes que precisam controlar os bytes: quadros partidos,
/// cabeçalhos inválidos, origem em outro IP de loopback e detecção de conexão fechada.
/// </summary>
internal sealed class RawModbusClient : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private readonly Socket _socket;
    private ushort _transactionId;

    private RawModbusClient(Socket socket) => _socket = socket;

    /// <summary>Conecta a <paramref name="server"/>, com origem em <paramref name="local"/> se dado (ex.: 127.0.0.2).</summary>
    public static async Task<RawModbusClient> ConnectAsync(IPEndPoint server, IPAddress? local = null)
    {
        var socket = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        if (local is not null)
        {
            socket.Bind(new IPEndPoint(local, 0));
        }

        await socket.ConnectAsync(server);
        return new RawModbusClient(socket);
    }

    public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint!;

    /// <summary>Monta um quadro MBAP com a PDU dada e o próximo número de transação.</summary>
    public byte[] Frame(byte unitId, params byte[] pdu)
    {
        var frame = new byte[MbapHeader.Size + pdu.Length];
        MbapHeader.WriteResponse(frame, ++_transactionId, unitId, pdu.Length);
        pdu.CopyTo(frame, MbapHeader.Size);
        return frame;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> bytes) => await _socket.SendAsync(bytes);

    /// <summary>Envia uma requisição e devolve a PDU da resposta, conferindo o número de transação.</summary>
    public async Task<byte[]> RequestAsync(byte unitId, params byte[] pdu)
    {
        byte[] frame = Frame(unitId, pdu);
        await SendAsync(frame);
        (ushort transactionId, byte[] response) = await ReceiveAsync(DefaultTimeout)
            ?? throw new TimeoutException("O servidor não respondeu.");
        Assert.Equal(_transactionId, transactionId);
        return response;
    }

    /// <summary>Próxima resposta (transação e PDU); nula se nada chegar no tempo dado ou se a conexão fechar.</summary>
    public async Task<(ushort TransactionId, byte[] Pdu)?> ReceiveAsync(TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            var header = new byte[MbapHeader.Size];
            if (!await ReceiveExactlyAsync(header, cancel.Token))
            {
                return null;
            }

            var pdu = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1];
            if (!await ReceiveExactlyAsync(pdu, cancel.Token))
            {
                return null;
            }

            return (BinaryPrimitives.ReadUInt16BigEndian(header), pdu);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Se o servidor fechou a conexão (leitura devolve 0 bytes ou erro) dentro do tempo dado.</summary>
    public async Task<bool> WasClosedByServerAsync(TimeSpan? timeout = null)
    {
        using var cancel = new CancellationTokenSource(timeout ?? DefaultTimeout);
        var buffer = new byte[MbapHeader.MaxAduLength];
        try
        {
            while (true)
            {
                if (await _socket.ReceiveAsync(buffer, SocketFlags.None, cancel.Token) == 0)
                {
                    return true;
                }
            }
        }
        catch (SocketException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose() => _socket.Dispose();

    private async Task<bool> ReceiveExactlyAsync(Memory<byte> buffer, CancellationToken token)
    {
        int total = 0;
        try
        {
            while (total < buffer.Length)
            {
                int read = await _socket.ReceiveAsync(buffer[total..], SocketFlags.None, token);
                if (read == 0)
                {
                    return false;
                }

                total += read;
            }
        }
        catch (SocketException)
        {
            return false;
        }

        return true;
    }
}
