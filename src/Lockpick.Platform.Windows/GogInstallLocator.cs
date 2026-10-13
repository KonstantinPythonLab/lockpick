using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Lockpick.Platform.Windows;

public sealed record InstalledGame(string ProductId, string Name, string InstallPath, string? ExecutablePath, string? Version);

/// <summary>Reads GOG installation records from the registry (read-only).</summary>
[SupportedOSPlatform("windows")]
public static class GogInstallLocator
{
    private static readonly string[] RootKeys =
    [
        @"SOFTWARE\WOW6432Node\GOG.com\Games",
        @"SOFTWARE\GOG.com\Games",
    ];

    public static InstalledGame? Find(string productId)
    {
        foreach (var root in RootKeys)
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{root}\{productId}");
            if (key?.GetValue("path") is string path)
            {
                return new InstalledGame(
                    productId,
                    key.GetValue("gameName") as string ?? productId,
                    path,
                    key.GetValue("exe") as string,
                    key.GetValue("ver") as string);
            }
        }

        return null;
    }
}
