using System.Diagnostics;
using System.Net;
using FluentModbus;

namespace IndustrialSim.SimRun.NetworkLoad;

/// <summary>
/// Mestre Modbus TCP de teste para uma planta, pela rede, como um CLP: liga a bomba e, a cada 100 ms, lê nível e
/// pressão (FC 4), escreve as duas válvulas (FC 16) e o heartbeat (FC 6). O controle é o proporcional simples do
/// <c>simrun carga</c> (Etapa 5): serve só para a planta receber comandos que mudam.
/// </summary>
/// <remarks>
/// Usa a FluentModbus (MIT), um mestre independente do servidor do projeto (D36). Qualquer erro (exceção Modbus,
/// time-out, conexão caída) conta como erro e o mestre reconecta depois de 1 s. O IP do mestre precisa estar
/// autorizado na planta antes de conectar (pareamento, D38).
/// </remarks>
internal sealed class LoadMaster(IPEndPoint endPoint, byte unitId = 1)
{
    public static readonly TimeSpan ScanPeriod = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly List<double> _scanMs = [];
    private long _scans;
    private long _errors;
    private long _reconnects;
    private string? _lastError;

    public IPEndPoint EndPoint { get; } = endPoint;

    public long Scans => Interlocked.Read(ref _scans);

    public long Errors => Interlocked.Read(ref _errors);

    public long Reconnects => Interlocked.Read(ref _reconnects);

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    /// <summary>Durações das varreduras completas (3 transações), em ms.</summary>
    public double[] ScanDurations()
    {
        lock (_gate)
        {
            return [.. _scanMs];
        }
    }

    /// <summary>Roda até o cancelamento; não lança exceção por falha de comunicação.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ushort heartbeat = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = new ModbusTcpClient { ConnectTimeout = (int)RequestTimeout.TotalMilliseconds };
            try
            {
                await Task.Run(() => client.Connect(EndPoint, ModbusEndianness.BigEndian), cancellationToken).ConfigureAwait(false);
                await WithTimeout(token => client.WriteSingleCoilAsync(unitId, 0, true, token), cancellationToken).ConfigureAwait(false);

                using var timer = new PeriodicTimer(ScanPeriod);
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    long start = Stopwatch.GetTimestamp();
                    Memory<short> measured = await WithTimeout(token => client.ReadInputRegistersAsync<short>(unitId, 0, 2, token), cancellationToken).ConfigureAwait(false);
                    short level = measured.Span[0];
                    short pressure = measured.Span[1];

                    // Pontos de ajuste no meio da faixa (0–27648): nível em 50 %, pressão em 62,5 %.
                    short[] commands = [Command(22748 + (13824 - level)), Command(13824 + (17280 - pressure))];
                    await WithTimeout(token => client.WriteMultipleRegistersAsync(unitId, 0, commands, token), cancellationToken).ConfigureAwait(false);
                    heartbeat++;
                    await WithTimeout(token => client.WriteSingleRegisterAsync(unitId, 10, heartbeat, token), cancellationToken).ConfigureAwait(false);

                    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    lock (_gate)
                    {
                        _scanMs.Add(ms);
                    }

                    Interlocked.Increment(ref _scans);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Exceção Modbus, time-out ou conexão caída: conta, espera e reconecta.
                Interlocked.Increment(ref _errors);
                lock (_gate)
                {
                    _lastError = $"{ex.GetType().Name}: {ex.Message}";
                }

                try
                {
                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                Interlocked.Increment(ref _reconnects);
            }
        }
    }

    private static short Command(int counts) => (short)Math.Clamp(counts, 0, 27648);

    private static async Task WithTimeout(Func<CancellationToken, Task> request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        await request(timeout.Token).ConfigureAwait(false);
    }

    private static async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        return await request(timeout.Token).ConfigureAwait(false);
    }
}
