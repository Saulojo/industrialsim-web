using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace IndustrialSim.Modbus.Tests;

// Arquivo compartilhado (por link) com os testes das plantas e do servidor.
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
            ?? throw new TimeoutException(
                $"O servidor não respondeu. Pool de threads: {ThreadPool.ThreadCount} threads, {ThreadPool.PendingWorkItemCount} itens na fila.");
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
