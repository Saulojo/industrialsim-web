using System.Diagnostics;

namespace HartPoc.Server;

/// <summary>Avança o tempo do instrumento simulado pelo relógio do servidor, a cada 50 ms.</summary>
public sealed class DeviceClock(HartDevice device) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        var last = Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = Stopwatch.GetTimestamp();
                device.Advance(Stopwatch.GetElapsedTime(last, now).TotalSeconds);
                last = now;
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do servidor.
        }
    }
}
