using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Lockpick.Core.Fingerprinting;

/// <summary>On-disk identity of a game executable.</summary>
public sealed record ExecutableFingerprint(string Path, long FileSize, string Sha256, PeImageInfo? Pe)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string ShortHash => Sha256.Length >= 12 ? Sha256[..12] : Sha256;
}

public interface IFingerprintProvider
{
    /// <summary>Computes (or returns a cached) fingerprint. Throws <see cref="IOException"/> when the file can't be read.</summary>
    ExecutableFingerprint Compute(string path);
}

/// <summary>
/// SHA-256 + PE header fingerprinting with a cache keyed on (path, size, last write time) so polling
/// for processes does not rehash the executable every scan.
/// </summary>
public sealed class FileFingerprintProvider : IFingerprintProvider
{
    private readonly ConcurrentDictionary<(string Path, long Size, DateTime Modified), ExecutableFingerprint> _cache = new();

    public ExecutableFingerprint Compute(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Executable not found.", fullPath);
        }

        var key = (fullPath.ToUpperInvariant(), info.Length, info.LastWriteTimeUtc);
        return _cache.GetOrAdd(key, _ => ComputeUncached(fullPath));
    }

    public static ExecutableFingerprint ComputeUncached(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
        var header = new byte[PeImageInfo.HeaderProbeSize];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        var pe = PeImageInfo.TryParse(header.AsSpan(0, read), out var parsed) ? parsed : null;
        stream.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return new ExecutableFingerprint(path, stream.Length, hash, pe);
    }
}
