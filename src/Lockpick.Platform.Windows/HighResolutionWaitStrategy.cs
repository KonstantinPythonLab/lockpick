using System.Runtime.Versioning;
using Lockpick.Core.Monitoring;
using Microsoft.Win32.SafeHandles;

namespace Lockpick.Platform.Windows;

/// <summary>
/// Sleeps using a high-resolution waitable timer (Windows 10 1803+), giving ~1 ms precision without
/// raising the global timer resolution. Falls back to the portable wait if the timer is unavailable.
/// One instance must only be used by one thread at a time.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HighResolutionWaitStrategy : IWaitStrategy, IDisposable
{
    private readonly TimerWaitHandle? _timer;

    public HighResolutionWaitStrategy()
    {
        var handle = NativeMethods.CreateWaitableTimerEx(0, null, NativeMethods.CreateWaitableTimerHighResolution, NativeMethods.TimerAllAccess);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return;
        }

        _timer = new TimerWaitHandle(handle);
    }

    public bool IsHighResolution => _timer is not null;

    public void Wait(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        if (_timer is null)
        {
            DefaultWaitStrategy.Instance.Wait(duration, cancellationToken);
            return;
        }

        // Negative due time = relative, in 100 ns units.
        var due = -duration.Ticks;
        if (!NativeMethods.SetWaitableTimer(_timer.SafeWaitHandle, in due, 0, 0, 0, false))
        {
            DefaultWaitStrategy.Instance.Wait(duration, cancellationToken);
            return;
        }

        WaitHandle.WaitAny([_timer, cancellationToken.WaitHandle]);
    }

    public void Dispose() => _timer?.Dispose();

    private sealed class TimerWaitHandle : WaitHandle
    {
        public TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }
}
