using Lockpick.Core.Adapters;
using Lockpick.Core.Fingerprinting;
using Lockpick.Core.Memory;
using Lockpick.Core.Processes;

namespace Lockpick.Core.Profiles;

public static class ProfileMatcher
{
    /// <summary>
    /// Exact-match identification: a build is supported only if its SHA-256 and size match a profile.
    /// Near misses (same file name or PE timestamp) are reported to make unsupported builds diagnosable.
    /// </summary>
    public static ProfileMatch Match(IEnumerable<VersionProfile> profiles, ExecutableFingerprint fingerprint)
    {
        var candidates = profiles.ToList();
        var exact = candidates.FirstOrDefault(p =>
            string.Equals(p.Executable.Sha256, fingerprint.Sha256, StringComparison.OrdinalIgnoreCase)
            && p.Executable.FileSize == fingerprint.FileSize);
        if (exact is not null)
        {
            return ProfileMatch.Supported(exact);
        }

        if (fingerprint.Pe is { } pe)
        {
            if (!pe.Is32Bit && candidates.All(p => p.Executable.Machine == PeMachine.X86))
            {
                return ProfileMatch.Unsupported($"Unsupported architecture {pe.Machine}; known builds are 32-bit.");
            }

            var sameStamp = candidates.FirstOrDefault(p => p.Executable.TimeDateStamp == pe.TimeDateStamp);
            if (sameStamp is not null)
            {
                return ProfileMatch.Unsupported(
                    $"Unknown build (SHA-256 {fingerprint.ShortHash}…). Same link timestamp as {sameStamp.Id}, so the file was probably modified or patched.");
            }
        }

        return ProfileMatch.Unsupported($"Unknown build (SHA-256 {fingerprint.ShortHash}…, {fingerprint.FileSize:N0} bytes). No address profile is available.");
    }
}

/// <summary>Checks that a running process image matches what a profile was built from.</summary>
public static class ImageVerifier
{
    public static IReadOnlyList<string> Verify(IAttachedProcess process, VersionProfile profile)
    {
        var problems = new List<string>();
        var exe = profile.Executable;
        var module = process.MainModule;

        if (module.BaseAddress != exe.ImageBase)
        {
            // Absolute addresses in profiles assume the preferred base; a moved image is unsafe to read.
            problems.Add($"image loaded at 0x{module.BaseAddress:X}, expected 0x{exe.ImageBase:X}");
        }

        if (module.Size != 0 && module.Size != exe.SizeOfImage)
        {
            problems.Add($"module size 0x{module.Size:X}, expected 0x{exe.SizeOfImage:X}");
        }

        var header = new byte[PeImageInfo.HeaderProbeSize];
        if (!process.TryRead(module.BaseAddress, header))
        {
            problems.Add("could not read the in-memory PE header");
        }
        else if (!PeImageInfo.TryParse(header, out var pe) || pe is null)
        {
            problems.Add("in-memory PE header is invalid");
        }
        else
        {
            if (pe.TimeDateStamp != exe.TimeDateStamp)
            {
                problems.Add($"in-memory link timestamp 0x{pe.TimeDateStamp:X8}, expected 0x{exe.TimeDateStamp:X8}");
            }

            if (pe.SizeOfImage != exe.SizeOfImage)
            {
                problems.Add($"in-memory SizeOfImage 0x{pe.SizeOfImage:X}, expected 0x{exe.SizeOfImage:X}");
            }
        }

        foreach (var anchor in profile.Anchors)
        {
            var actual = new byte[anchor.Bytes.Length];
            if (!process.TryRead(anchor.Address, actual))
            {
                problems.Add($"anchor '{anchor.Name}' at 0x{anchor.Address:X} is unreadable");
            }
            else if (!actual.AsSpan().SequenceEqual(anchor.Bytes))
            {
                problems.Add($"anchor '{anchor.Name}' at 0x{anchor.Address:X} differs (found {Convert.ToHexString(actual)})");
            }
        }

        return problems;
    }

    /// <summary>The readable ranges a session for this profile may touch.</summary>
    public static IReadOnlyList<AddressRange> AllowedRanges(VersionProfile profile) => [profile.ImageRange];
}
