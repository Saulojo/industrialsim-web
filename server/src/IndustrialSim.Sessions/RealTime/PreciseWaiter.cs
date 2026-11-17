using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace IndustrialSim.Sessions.RealTime;

/// <summary>Espera usada pelo laço entre um passo e outro. Cada thread do laço tem a sua.</summary>
public interface IPreciseWaiter : IDisposable
{
    /// <summary>Nome do mecanismo, para os logs e o relatório.</summary>
    string Kind { get; }

    /// <summary>Bloqueia a thread por aproximadamente <paramref name="duration"/>; durações não positivas retornam na hora.</summary>
    void Wait(TimeSpan duration);
}

/// <summary>Escolhe a espera do sistema operacional.</summary>
public static class PreciseWaiter
{
    /// <summary>
    /// No Windows (sistema do servidor do laboratório, D10), um temporizador de alta resolução: o
    /// <c>Thread.Sleep</c> do Windows tem resolução de cerca de 15,6 ms, maior que o passo de 10 ms. Nos outros
    /// sistemas (o CI roda em Linux), <c>Thread.Sleep</c>, que lá tem resolução de cerca de 1 ms.
    /// </summary>
    public static IPreciseWaiter Create()
    {
        if (OperatingSystem.IsWindows() && WindowsHighResolutionWaiter.TryCreate() is { } waiter)
        {
            return waiter;
        }

        return new SleepWaiter();
    }
}

/// <summary>Espera por <see cref="Thread.Sleep(TimeSpan)"/>, com no mínimo 1 ms.</summary>
internal sealed class SleepWaiter : IPreciseWaiter
{
    private static readonly TimeSpan OneMillisecond = TimeSpan.FromMilliseconds(1);

    public string Kind => "thread-sleep";

    public void Wait(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        Thread.Sleep(duration < OneMillisecond ? OneMillisecond : duration);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Temporizador do Windows criado com <c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c> (Windows 10 1803 em diante),
/// que acorda com resolução abaixo de 1 ms sem mudar a resolução do relógio do sistema inteiro
/// (documentação da Win32: <c>CreateWaitableTimerExW</c> e <c>SetWaitableTimer</c>).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsHighResolutionWaiter : IPreciseWaiter
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private const uint Infinite = 0xFFFFFFFF;

    private readonly SafeWaitHandle _timer;

    private WindowsHighResolutionWaiter(SafeWaitHandle timer) => _timer = timer;

    public string Kind => "windows-high-resolution-timer";

    /// <summary>Nulo se o sistema não oferecer o temporizador de alta resolução.</summary>
    public static WindowsHighResolutionWaiter? TryCreate()
    {
        SafeWaitHandle timer = CreateWaitableTimerExW(0, 0, CreateWaitableTimerHighResolution, TimerAllAccess);
        if (timer.IsInvalid)
        {
            timer.Dispose();
            return null;
        }

        return new WindowsHighResolutionWaiter(timer);
    }

    public void Wait(TimeSpan duration)
    {
        ObjectDisposedException.ThrowIf(_timer.IsClosed, this);

        // Tempo relativo: valor negativo, em unidades de 100 ns (as mesmas do TimeSpan.Ticks).
        long dueTime = -duration.Ticks;
        if (dueTime >= 0)
        {
            return;
        }

        if (SetWaitableTimer(_timer, in dueTime, 0, 0, 0, 0) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _ = WaitForSingleObject(_timer, Infinite);
    }

    public void Dispose() => _timer.Dispose();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeWaitHandle CreateWaitableTimerExW(nint timerAttributes, nint timerName, uint flags, uint desiredAccess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period, nint completionRoutine, nint completionArgument, int resume);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
