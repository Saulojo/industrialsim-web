using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialSim.Modbus;

/// <summary>Limites do servidor Modbus de uma planta.</summary>
public sealed record ModbusServerOptions
{
    /// <summary>Conexões ao mesmo tempo (pendentes ou autorizadas); as excedentes são fechadas na hora.</summary>
    public int MaxConnections { get; init; } = 8;

    /// <summary>Tempo sem tráfego até o TCP sondar o mestre (keepalive), para derrubar conexões mortas.</summary>
    public TimeSpan KeepAliveTime { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConnections, 1, nameof(MaxConnections));
        ArgumentOutOfRangeException.ThrowIfLessThan(KeepAliveTime, TimeSpan.FromSeconds(1), nameof(KeepAliveTime));
    }
}

/// <summary>Uma conexão de mestre Modbus.</summary>
/// <param name="Id">Número da conexão no servidor.</param>
/// <param name="Remote">IP e porta do mestre.</param>
/// <param name="ConnectedUtc">Abertura da conexão.</param>
/// <param name="Authorized">Se já passou pelo pareamento (senão, está pendente).</param>
/// <param name="Requests">Requisições atendidas.</param>
public sealed record ModbusConnectionInfo(long Id, IPEndPoint Remote, DateTimeOffset ConnectedUtc, bool Authorized, long Requests);

/// <summary>
/// Servidor Modbus TCP de uma planta: escuta numa porta, passa cada conexão pelo pareamento
/// (<see cref="IConnectionGate"/>) e atende as requisições pelo <see cref="ModbusDevice"/>.
/// </summary>
/// <remarks>
/// Cada conexão atende uma requisição por vez, na ordem em que chegam. Enquanto o pareamento não decide, a
/// conexão fica aberta e as requisições que chegam são lidas e descartadas, sem resposta: assim o fluxo TCP não
/// perde o enquadramento, e um comando antigo não é executado depois da autorização. Um cabeçalho MBAP inválido
/// fecha a conexão; uma requisição inválida recebe resposta de exceção e a conexão continua.
/// </remarks>
public sealed class ModbusTcpServer : IDisposable
{
    private readonly ModbusDevice _device;
    private readonly IConnectionGate _gate;
    private readonly ModbusServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<long, Connection> _connections = new();
    private readonly Lock _gateLock = new();
    private Socket? _listener;
    private long _nextConnectionId;
    private bool _disposed;

    public ModbusTcpServer(
        ModbusDevice device,
        IConnectionGate gate,
        ModbusServerOptions? options = null,
        TimeProvider? time = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(gate);
        _device = device;
        _gate = gate;
        _options = options ?? new ModbusServerOptions();
        _options.Validate();
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    public ModbusDevice Device => _device;

    /// <summary>Endereço e porta em que o servidor escuta; nulo antes de <see cref="Start"/>.</summary>
    public IPEndPoint? LocalEndPoint { get; private set; }

    /// <summary>Conexões abertas, da mais antiga para a mais nova.</summary>
    public IReadOnlyList<ModbusConnectionInfo> Connections =>
        [.. _connections.Values.OrderBy(c => c.Id).Select(c => c.ToInfo())];

    /// <summary>
    /// Passa a escutar em <paramref name="endPoint"/>. Lança <see cref="SocketException"/> se a porta estiver
    /// em uso; nesse caso o servidor continua parado e pode tentar outra porta.
    /// </summary>
    public void Start(IPEndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        lock (_gateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listener is not null)
            {
                throw new InvalidOperationException("O servidor Modbus já está escutando.");
            }

            var listener = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Impede outro processo de se ligar à mesma porta com SO_REUSEADDR e roubar as conexões.
                    listener.ExclusiveAddressUse = true;
                }

                if (endPoint.AddressFamily == AddressFamily.InterNetworkV6 && endPoint.Address.Equals(IPAddress.IPv6Any))
                {
                    listener.DualMode = true;
                }

                listener.Bind(endPoint);
                listener.Listen(16);
            }
            catch
            {
                listener.Dispose();
                throw;
            }

            _listener = listener;
            LocalEndPoint = (IPEndPoint)listener.LocalEndPoint!;
            _ = Task.Run(() => AcceptLoopAsync(listener));
        }

        ModbusLog.ServerStarted(_logger, LocalEndPoint);
    }

    /// <summary>Fecha as conexões vindas de <paramref name="address"/> (retirada da autorização). Devolve quantas.</summary>
    public int CloseConnections(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        IPAddress normalized = Normalize(address);
        int closed = 0;
        foreach (Connection connection in _connections.Values)
        {
            if (connection.Address.Equals(normalized))
            {
                connection.Close();
                closed++;
            }
        }

        if (closed > 0)
        {
            ModbusLog.ConnectionsClosedByAddress(_logger, closed, normalized);
        }

        return closed;
    }

    /// <summary>
    /// Para de escutar e fecha todas as conexões. Ao retornar, a porta está livre e os sockets estão fechados;
    /// as tarefas das conexões terminam sozinhas em seguida, sem bloquear quem chamou (esperar por elas aqui
    /// prenderia uma thread do pool enquanto elas precisam do pool para terminar).
    /// </summary>
    public void Dispose()
    {
        Socket? listener;
        lock (_gateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            listener = _listener;
        }

        _stopping.Cancel();
        listener?.Dispose();
        foreach (Connection connection in _connections.Values)
        {
            _ = connection.Close();
        }

        if (LocalEndPoint is not null)
        {
            ModbusLog.ServerStopped(_logger, LocalEndPoint);
        }
    }

    /// <summary>Endereço IPv4 mapeado em IPv6 (::ffff:a.b.c.d) volta a ser IPv4, para comparar com a lista do pareamento.</summary>
    public static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private async Task AcceptLoopAsync(Socket listener)
    {
        CancellationToken stopping = _stopping.Token;
        while (!stopping.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptAsync(stopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                || (ex is SocketException && stopping.IsCancellationRequested))
            {
                return;
            }
            catch (SocketException ex)
            {
                // Uma falha numa conexão que chegava (ex.: o mestre desistiu) não para o servidor.
                ModbusLog.AcceptFailed(_logger, ex, LocalEndPoint!);
                continue;
            }

            Accept(socket);
        }
    }

    private void Accept(Socket socket)
    {
        var remote = (IPEndPoint)socket.RemoteEndPoint!;
        if (_connections.Count >= _options.MaxConnections)
        {
            ModbusLog.ConnectionLimitReached(_logger, remote, LocalEndPoint!.Port, _options.MaxConnections);
            socket.Dispose();
            return;
        }

        ConfigureSocket(socket);
        long id = Interlocked.Increment(ref _nextConnectionId);
        var connection = new Connection(id, socket, remote, _time.GetUtcNow(), _stopping.Token);
        _connections[id] = connection;
        ModbusLog.ConnectionOpened(_logger, id, remote, LocalEndPoint!.Port);
        connection.Task = Task.Run(() => ServeAsync(connection));
    }

    private void ConfigureSocket(Socket socket)
    {
        socket.NoDelay = true;
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, (int)_options.KeepAliveTime.TotalSeconds);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }
        catch (SocketException)
        {
            // Sistema sem ajuste fino de keepalive: fica o padrão do sistema.
        }
    }

    private async Task ServeAsync(Connection connection)
    {
        CancellationToken token = connection.Token;
        Task<IDisposable?> authorization = Task.FromResult<IDisposable?>(null);
        Task<bool>? read = null;
        bool authorizationTaken = false;
        try
        {
            authorization = _gate.AuthorizeAsync(connection.Address, token);
            read = ReadFrameAsync(connection, token);

            // Pendente: lê e descarta as requisições até o pareamento decidir.
            int discarded = 0;
            while (!authorization.IsCompleted)
            {
                if (await Task.WhenAny(authorization, read).ConfigureAwait(false) == read)
                {
                    if (!await read.ConfigureAwait(false))
                    {
                        return;
                    }

                    discarded++;
                    read = ReadFrameAsync(connection, token);
                }
            }

            authorizationTaken = true;
            using IDisposable? lease = await authorization.ConfigureAwait(false);
            if (lease is null)
            {
                ModbusLog.ConnectionRefused(_logger, connection.Id, connection.Remote);
                return;
            }

            connection.Authorized = true;
            ModbusLog.ConnectionAuthorized(_logger, connection.Id, connection.Remote, discarded);
            string author = connection.Address.ToString();
            while (await read.ConfigureAwait(false))
            {
                await RespondAsync(connection, author, token).ConfigureAwait(false);
                read = ReadFrameAsync(connection, token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // Conexão fechada pelo mestre, pela retirada da autorização ou pelo encerramento do servidor.
        }
        finally
        {
            _ = connection.Close();
            _connections.TryRemove(connection.Id, out _);
            connection.ReleaseToken();

            // A leitura em curso termina com erro quando o fluxo fecha; o erro é esperado e fica observado aqui.
            _ = read?.ContinueWith(static t => t.Exception, TaskScheduler.Default);
            if (!authorizationTaken)
            {
                // O mestre saiu antes da decisão: se ela ainda vier com autorização, o objeto é liberado.
                _ = authorization.ContinueWith(
                    static t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            t.Result?.Dispose();
                        }

                        return t.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            ModbusLog.ConnectionClosed(_logger, connection.Id, connection.Remote, connection.Requests);
        }
    }

    /// <summary>
    /// Lê um quadro inteiro para o buffer da conexão. Falso se o mestre fechou a conexão; fecha a conexão se o
    /// cabeçalho for inválido.
    /// </summary>
    private async Task<bool> ReadFrameAsync(Connection connection, CancellationToken token)
    {
        Memory<byte> buffer = connection.Request;
        int read = await connection.Stream.ReadAtLeastAsync(buffer[..MbapHeader.Size], MbapHeader.Size, throwOnEndOfStream: false, token)
            .ConfigureAwait(false);
        if (read < MbapHeader.Size)
        {
            return false;
        }

        MbapHeader header = MbapHeader.Read(buffer.Span);
        if (!header.IsValid)
        {
            ModbusLog.InvalidFrame(_logger, connection.Id, connection.Remote, header.ProtocolId, header.Length);
            return false;
        }

        read = await connection.Stream.ReadAtLeastAsync(
            buffer.Slice(MbapHeader.Size, header.PduLength), header.PduLength, throwOnEndOfStream: false, token).ConfigureAwait(false);
        return read == header.PduLength;
    }

    private async Task RespondAsync(Connection connection, string author, CancellationToken token)
    {
        MbapHeader header = MbapHeader.Read(connection.Request.Span);
        int pduLength = _device.Process(
            header.UnitId,
            connection.Request.Span.Slice(MbapHeader.Size, header.PduLength),
            connection.Response.Span[MbapHeader.Size..],
            author);
        MbapHeader.WriteResponse(connection.Response.Span, header.TransactionId, header.UnitId, pduLength);
        connection.CountRequest();

        byte function = connection.Response.Span[MbapHeader.Size];
        if ((function & 0x80) != 0)
        {
            ModbusLog.ExceptionResponse(_logger, connection.Id, connection.Response.Span[MbapHeader.Size + 1], (byte)(function & 0x7F));
        }

        await connection.Stream.WriteAsync(connection.Response[..(MbapHeader.Size + pduLength)], token).ConfigureAwait(false);
    }

    private sealed class Connection
    {
        private readonly CancellationTokenSource _closing;
        private long _requests;
        private int _authorized;

        public Connection(long id, Socket socket, IPEndPoint remote, DateTimeOffset connectedUtc, CancellationToken stopping)
        {
            Id = id;
            Socket = socket;
            Stream = new NetworkStream(socket, ownsSocket: true);
            Remote = remote;
            Address = Normalize(remote.Address);
            ConnectedUtc = connectedUtc;
            _closing = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            Token = _closing.Token;
        }

        public long Id { get; }

        public Socket Socket { get; }

        public NetworkStream Stream { get; }

        public IPEndPoint Remote { get; }

        public IPAddress Address { get; }

        public DateTimeOffset ConnectedUtc { get; }

        public CancellationToken Token { get; }

        public Memory<byte> Request { get; } = new byte[MbapHeader.MaxAduLength];

        public Memory<byte> Response { get; } = new byte[MbapHeader.MaxAduLength];

        public Task Task { get; set; } = Task.CompletedTask;

        public long Requests => Interlocked.Read(ref _requests);

        public bool Authorized
        {
            get => Volatile.Read(ref _authorized) != 0;
            set => Volatile.Write(ref _authorized, value ? 1 : 0);
        }

        public void CountRequest() => Interlocked.Increment(ref _requests);

        /// <summary>Libera o cancelamento da conexão (ligado ao do servidor) quando ela termina.</summary>
        public void ReleaseToken() => _closing.Dispose();

        /// <summary>Fecha a conexão; devolve a tarefa que a atende.</summary>
        public Task Close()
        {
            try
            {
                _closing.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            Stream.Dispose();
            return Task;
        }

        public ModbusConnectionInfo ToInfo() => new(Id, Remote, ConnectedUtc, Authorized, Requests);
    }
}
