using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockpick.App.Services;
using Lockpick.GameAdapter.RE1;
using Lockpick.Platform.Windows;

namespace Lockpick.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
        : base("Settings", "Icon.Settings")
    {
        _services = services;
        GameExecutablePath = services.Settings.Current.GameExecutablePath ?? string.Empty;
        var gog = GogInstallLocator.Find(Re1Adapter.GogProductId);
        DetectedInstall = gog is null
            ? "No GOG install of Resident Evil found in the registry."
            : $"GOG: {gog.InstallPath} (version {gog.Version ?? "unknown"})";
        ProfileDirectory = services.Settings.Current.ProfileDirectory ?? AppPaths.Profiles;
        LoadedProfiles = string.Join(Environment.NewLine, services.Adapter.Profiles.Select(p => $"{p.Id} — {p.DisplayName}"));
        CheckInstalledBuild();
    }

    public string DetectedInstall { get; }

    public string ProfileDirectory { get; }

    public string LoadedProfiles { get; }

    [ObservableProperty]
    public partial string GameExecutablePath { get; set; }

    [ObservableProperty]
    public partial string BuildCheckResult { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool BuildSupported { get; private set; }

    [ObservableProperty]
    public partial string SaveResult { get; private set; } = string.Empty;

    [RelayCommand]
    private void CheckInstalledBuild()
    {
        var path = string.IsNullOrWhiteSpace(GameExecutablePath) ? _services.ResolveInstalledExecutable() : GameExecutablePath.Trim();
        if (path is null)
        {
            BuildSupported = false;
            BuildCheckResult = "No executable configured or detected.";
            return;
        }

        try
        {
            var fp = _services.Fingerprints.Compute(path);
            var match = _services.Adapter.Identify(fp);
            BuildSupported = match.IsSupported;
            BuildCheckResult = string.Create(
                CultureInfo.InvariantCulture,
                $"{fp.Path}\n{fp.FileSize:N0} bytes · SHA-256 {fp.Sha256}\n{(match.IsSupported ? "SUPPORTED" : "NOT SUPPORTED")} — {match.Reason}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            BuildSupported = false;
            BuildCheckResult = $"Cannot read {path}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        var trimmed = GameExecutablePath.Trim();
        try
        {
            _services.Settings.Save(_services.Settings.Current with { GameExecutablePath = trimmed.Length == 0 ? null : trimmed });
            SaveResult = "Saved.";
            CheckInstalledBuild();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveResult = $"Could not save settings: {ex.Message}";
        }
    }
}
