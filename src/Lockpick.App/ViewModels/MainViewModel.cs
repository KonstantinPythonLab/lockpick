using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lockpick.App.Services;
using Lockpick.Core.Monitoring;

namespace Lockpick.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public MainViewModel(AppServices services)
    {
        State = new LiveState(services.Source);
        Diagnostics = new DiagnosticsViewModel(services, State);
        Pages = [new DashboardViewModel(State), Diagnostics, new SettingsViewModel(services)];
        SelectedPage = Pages[0];
        Version = $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";
        State.PropertyChanged += OnStateChanged;
        UpdateHeader();
    }

    public LiveState State { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public string Version { get; }

    [ObservableProperty]
    public partial PageViewModel SelectedPage { get; set; }

    [ObservableProperty]
    public partial string HeaderStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsConnected { get; private set; }

    [ObservableProperty]
    public partial bool IsUnsupported { get; private set; }

    [ObservableProperty]
    public partial bool IsReplaying { get; private set; }

    public void Dispose() => Diagnostics.Dispose();

    private PageViewModel? _visiblePage;

    partial void OnSelectedPageChanged(PageViewModel value)
    {
        _visiblePage?.OnVisibilityChanged(false);
        _visiblePage = value;
        value?.OnVisibilityChanged(true);
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveState.Status))
        {
            UpdateHeader();
        }
    }

    private void UpdateHeader()
    {
        var status = State.Status;
        IsConnected = status.State == ConnectionState.Attached;
        IsUnsupported = status.State == ConnectionState.Unsupported;
        IsReplaying = status.State == ConnectionState.Replaying;
        HeaderStatus = status.State switch
        {
            ConnectionState.Attached => $"CONNECTED · {status.Profile?.Id}",
            ConnectionState.Searching => "SEARCHING",
            ConnectionState.Unsupported => "UNSUPPORTED BUILD",
            ConnectionState.Replaying => "REPLAY",
            _ => "STOPPED",
        };
    }
}
