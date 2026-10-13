using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockpick.App.Services;
using Lockpick.Core.Logging;
using Lockpick.Core.Monitoring;
using Lockpick.Core.Profiles;
using Lockpick.Core.Recording;
using Lockpick.Core.State;
using Microsoft.Extensions.Logging;

namespace Lockpick.App.ViewModels;

public sealed record RawFieldRow(string Name, string Address, string Type, string Verification, string Value, string Status, bool IsTrusted);

public sealed record LogRow(string Time, string Level, string Category, string Message, bool IsWarning);

public sealed partial class DiagnosticsViewModel : PageViewModel, IDisposable
{
    private const int MaxLogRows = 400;

    private readonly AppServices _services;
    private readonly DispatcherTimer _statsTimer;
    private readonly ILogger _logger;
    private SessionRecorder? _recorder;

    public DiagnosticsViewModel(AppServices services, LiveState state)
        : base("Diagnostics", "Icon.Activity")
    {
        _services = services;
        _logger = services.LoggerFactory.CreateLogger("Diagnostics");
        State = state;
        LogFile = services.LogProvider.FilePath ?? "(file logging disabled)";
        TimerInfo = services.IsReplay ? "Replay" : services.HighResolutionTimer ? "High-resolution waitable timer" : "Default OS timer (≈15.6 ms granularity)";
        foreach (var entry in services.LogProvider.RecentEntries.TakeLast(MaxLogRows).Reverse())
        {
            LogRows.Add(ToRow(entry));
        }

        services.LogProvider.EntryLogged += entry => Dispatcher.UIThread.Post(() => AddLog(entry), DispatcherPriority.Background);
        state.PropertyChanged += OnStateChanged;
        state.Source.SnapshotChanged += OnSnapshotForRecording;
        _statsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => RefreshStats());
        RefreshRawFields();
    }

    public LiveState State { get; }

    public string LogFile { get; }

    public string TimerInfo { get; }

    public ObservableCollection<RawFieldRow> RawFields { get; } = [];

    public ObservableCollection<LogRow> LogRows { get; } = [];

    [ObservableProperty]
    public partial string PollStats { get; private set; } = "—";

    [ObservableProperty]
    public partial string RecordingStatus { get; private set; } = "Not recording";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand), nameof(StopRecordingCommand))]
    public partial bool IsRecording { get; private set; }

    public override void OnVisibilityChanged(bool visible)
    {
        if (visible)
        {
            RefreshStats();
            RefreshRawFields();
            _statsTimer.Start();
        }
        else
        {
            _statsTimer.Stop();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private void StartRecording()
    {
        var path = Path.Combine(AppPaths.Recordings, $"re1-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        var recorder = SessionRecorder.Create(path, _services.Adapter.GameId, State.Status.Profile?.Id, DateTimeOffset.UtcNow);
        if (State.Snapshot is { } current)
        {
            recorder.Append(current);
        }

        Volatile.Write(ref _recorder, recorder);
        IsRecording = true;
        RecordingStatus = $"Recording to {path}";
        _logger.LogInformation("Recording started: {Path}", path);
    }

    private bool CanStartRecording() => !IsRecording;

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private void StopRecording()
    {
        var recorder = Interlocked.Exchange(ref _recorder, null);
        if (recorder is null)
        {
            return;
        }

        recorder.Dispose();
        IsRecording = false;
        RecordingStatus = $"Saved {recorder.FrameCount} frames to {recorder.Path}";
        _logger.LogInformation("Recording saved: {Path} ({Frames} frames)", recorder.Path, recorder.FrameCount);
    }

    public void Dispose()
    {
        _statsTimer.Stop();
        Interlocked.Exchange(ref _recorder, null)?.Dispose();
    }

    private void OnSnapshotForRecording(object? sender, GameSnapshot snapshot) => Volatile.Read(ref _recorder)?.Append(snapshot);

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_statsTimer.IsEnabled)
        {
            RefreshRawFields();
        }
    }

    private void RefreshStats()
    {
        var s = State.Source.Statistics;
        PollStats = State.Status.State == ConnectionState.Attached
            ? string.Create(CultureInfo.InvariantCulture, $"{s.Polls:N0} polls ({s.FailedPolls:N0} failed) · {s.MeasuredPollHz:F1} Hz · read avg {s.AveragePollMs:F3} ms · max {s.MaxPollMs:F3} ms · last {s.LastPollMs:F3} ms")
            : State.Status.State == ConnectionState.Replaying ? $"{s.Polls:N0} frames replayed" : "Not attached";
    }

    private void RefreshRawFields()
    {
        RawFields.Clear();
        var profile = State.Status.Profile ?? (_services.Adapter.Profiles.Count > 0 ? _services.Adapter.Profiles[0] : null);
        if (profile is null)
        {
            return;
        }

        var raw = State.Snapshot?.RawFields;
        foreach (var f in profile.Fields)
        {
            var value = raw is not null && raw.TryGetValue(f.Name, out var v) ? v : (Field<long>?)null;
            RawFields.Add(new RawFieldRow(
                f.Name,
                $"0x{f.Address:X}",
                f.Type.ToString().ToLowerInvariant() + (f.Mask is { } m ? $" & 0x{m:X}" : string.Empty),
                f.Verification.ToString().ToLowerInvariant(),
                value is { HasValue: true } fv ? string.Create(CultureInfo.InvariantCulture, $"{fv.Value} (0x{fv.Value:X})") : "—",
                value?.Status.ToString() ?? "—",
                f.Verification >= Verification.Static));
        }
    }

    private void AddLog(LogEntry entry)
    {
        LogRows.Insert(0, ToRow(entry));
        if (LogRows.Count > MaxLogRows)
        {
            LogRows.RemoveAt(LogRows.Count - 1);
        }
    }

    private static LogRow ToRow(LogEntry e) => new(
        e.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
        e.Level.ToString().ToUpperInvariant(),
        e.Category,
        e.Exception is null ? e.Message : $"{e.Message} — {e.Exception.Split('\n')[0]}",
        e.Level >= LogLevel.Warning);
}
