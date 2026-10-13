using System.Diagnostics;
using Lockpick.Platform.Windows;
using Microsoft.Extensions.Logging;

namespace Lockpick.App.Services;

/// <summary>A game process frozen by ask mode, remembered in settings so a crashed Lockpick can unfreeze it next start.</summary>
public sealed record PausedGame(int ProcessId, DateTime StartTimeUtc);

/// <summary>
/// Freezes the game while the player answers an ask-mode question, and makes sure it always comes back: on resume, on
/// a watchdog timeout, on app exit, and — if Lockpick died while the game was frozen — at the next start.
/// </summary>
public sealed class GamePause : IDisposable
{
    /// <summary>Longest freeze; after this the game resumes even if the question is still open.</summary>
    public static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(10);

    private readonly SettingsStore _settings;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private int? _processId;
    private Timer? _watchdog;

    public GamePause(SettingsStore settings, ILogger log)
    {
        _settings = settings;
        _log = log;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Resume();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Resume();
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _processId is not null;
            }
        }
    }

    /// <summary>Freezes the process. Returns null on success, else why it couldn't.</summary>
    public string? Pause(int processId)
    {
        lock (_gate)
        {
            if (_processId is not null)
            {
                return null;
            }

            var started = StartTime(processId);
            if (started is null)
            {
                return "the game process is gone";
            }

            Remember(new PausedGame(processId, started.Value));
            if (ProcessSuspender.Suspend(processId) is { } error)
            {
                Remember(null);
                _log.LogWarning("Could not freeze the game: {Error}", error);
                return error;
            }

            _processId = processId;
            _watchdog = new Timer(_ => Resume(), null, MaxPause, Timeout.InfiniteTimeSpan);
            _log.LogInformation("Game frozen for a question (pid {Pid})", processId);
            return null;
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            _watchdog?.Dispose();
            _watchdog = null;
            if (_processId is not { } pid)
            {
                return;
            }

            _processId = null;
            if (ProcessSuspender.Resume(pid) is { } error)
            {
                _log.LogWarning("Could not resume the game: {Error}", error);
            }
            else
            {
                _log.LogInformation("Game resumed (pid {Pid})", pid);
            }

            Remember(null);
        }
    }

    /// <summary>Called at startup: resumes a game left frozen by a Lockpick that exited mid-question.</summary>
    public void RecoverFromCrash()
    {
        if (_settings.Current.PausedGame is not { } paused)
        {
            return;
        }

        if (StartTime(paused.ProcessId) == paused.StartTimeUtc)
        {
            var error = ProcessSuspender.Resume(paused.ProcessId);
            _log.LogWarning("Resumed a game left frozen by a previous Lockpick (pid {Pid}): {Result}", paused.ProcessId, error ?? "ok");
        }

        Remember(null);
    }

    public void Dispose() => Resume();

    private static DateTime? StartTime(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private void Remember(PausedGame? paused)
    {
        try
        {
            _settings.Save(_settings.Current with { PausedGame = paused });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not record the frozen game in settings");
        }
    }
}
