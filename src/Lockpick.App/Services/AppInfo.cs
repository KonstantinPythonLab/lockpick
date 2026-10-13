using System.Reflection;

namespace Lockpick.App.Services;

/// <summary>The app's version as released (e.g. "0.1.3" or "0.2.0-beta.1") — the single source for every display.</summary>
public static class AppInfo
{
    public static string Version { get; } = Resolve(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        typeof(AppInfo).Assembly.GetName().Version);

    /// <summary>
    /// Uses the informational version (the project's &lt;Version&gt;, keeping any pre-release suffix) without the
    /// "+commit" build metadata the SDK appends. The four-part assembly version ("0.1.3.0") is only a fallback.
    /// </summary>
    internal static string Resolve(string? informational, System.Version? assembly)
    {
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly?.ToString(3) ?? "0.0.0";
    }
}
