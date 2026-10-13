using Avalonia;

namespace Lockpick.App;

internal static class Program
{
    // Usage: Lockpick.exe [--replay <recording.jsonl>] [--verbose]
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
