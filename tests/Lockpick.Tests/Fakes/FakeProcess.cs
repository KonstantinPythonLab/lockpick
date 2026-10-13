using System.Buffers.Binary;
using Lockpick.Core.Fingerprinting;
using Lockpick.Core.Memory;
using Lockpick.Core.Processes;
using Lockpick.Core.Profiles;

namespace Lockpick.Tests.Fakes;

/// <summary>Sparse byte-addressable memory. Unwritten bytes are unreadable, like unmapped pages.</summary>
public sealed class FakeMemory : IProcessMemory
{
    private readonly Dictionary<ulong, byte> _bytes = [];

    public bool FailAllReads { get; set; }

    public HashSet<ulong> FailingAddresses { get; } = [];

    public int ReadCount { get; private set; }

    public FakeMemory Write(ulong address, ReadOnlySpan<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            _bytes[address + (ulong)i] = data[i];
        }

        return this;
    }

    public FakeMemory WriteU8(ulong address, byte value) => Write(address, [value]);

    public FakeMemory WriteI16(ulong address, short value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(b, value);
        return Write(address, b);
    }

    public FakeMemory WriteU32(ulong address, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        return Write(address, b);
    }

    public FakeMemory WriteI32(ulong address, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        return Write(address, b);
    }

    public bool TryRead(ulong address, Span<byte> destination)
    {
        ReadCount++;
        if (FailAllReads || FailingAddresses.Contains(address))
        {
            return false;
        }

        for (var i = 0; i < destination.Length; i++)
        {
            if (!_bytes.TryGetValue(address + (ulong)i, out var b))
            {
                return false;
            }

            destination[i] = b;
        }

        return true;
    }

    /// <summary>Writes a minimal PE header and every anchor so the image verifies against <paramref name="profile"/>.</summary>
    public FakeMemory WithVerifiedImage(VersionProfile profile)
    {
        Write(profile.Executable.ImageBase, PeHeaderBuilder.Build(profile.Executable.TimeDateStamp, (uint)profile.Executable.ImageBase, profile.Executable.SizeOfImage));
        foreach (var anchor in profile.Anchors)
        {
            Write(anchor.Address, anchor.Bytes);
        }

        // Every profile field starts readable (zero) so tests only set what they care about.
        foreach (var field in profile.Fields)
        {
            Write(field.Address, new byte[ValueTypeExtensions.SizeOf(field.Type)]);
        }

        return this;
    }
}

public sealed class FakeAttachedProcess(int processId, FakeMemory memory, ModuleImage module) : IAttachedProcess
{
    public int ProcessId { get; } = processId;

    public FakeMemory Memory { get; } = memory;

    public bool HasExited { get; set; }

    public bool Disposed { get; private set; }

    public ModuleImage MainModule { get; } = module;

    public bool TryRead(ulong address, Span<byte> destination) => !HasExited && Memory.TryRead(address, destination);

    public void Dispose() => Disposed = true;
}

public sealed class FakeProcessHost : IProcessHost
{
    public List<(ProcessCandidate Candidate, FakeAttachedProcess Process)> Running { get; } = [];

    public int OpenCount { get; private set; }

    public Exception? OpenFailure { get; set; }

    public FakeAttachedProcess Launch(int pid, string path, FakeMemory memory, ModuleImage module)
    {
        var process = new FakeAttachedProcess(pid, memory, module);
        Running.Add((new ProcessCandidate(pid, Path.GetFileNameWithoutExtension(path), path, DateTimeOffset.UtcNow), process));
        return process;
    }

    public void Exit(int pid)
    {
        foreach (var entry in Running.Where(r => r.Candidate.ProcessId == pid).ToList())
        {
            entry.Process.HasExited = true;
            Running.Remove(entry);
        }
    }

    public IReadOnlyList<ProcessCandidate> FindProcesses(IReadOnlyCollection<string> executableNames) =>
        Running.Where(r => executableNames.Contains(Path.GetFileName(r.Candidate.ExecutablePath!), StringComparer.OrdinalIgnoreCase))
            .Select(r => r.Candidate)
            .ToList();

    public IAttachedProcess OpenReadOnly(ProcessCandidate candidate)
    {
        OpenCount++;
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        return Running.Single(r => r.Candidate.ProcessId == candidate.ProcessId).Process;
    }
}

/// <summary>Returns preset fingerprints by path, so tests never need real executables.</summary>
public sealed class FakeFingerprintProvider : IFingerprintProvider
{
    public Dictionary<string, ExecutableFingerprint> ByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int ComputeCount { get; private set; }

    public ExecutableFingerprint Compute(string path)
    {
        ComputeCount++;
        return ByPath.TryGetValue(path, out var fp) ? fp : throw new FileNotFoundException("No fake fingerprint.", path);
    }

    public static ExecutableFingerprint For(VersionProfile profile, string path) =>
        new(path, profile.Executable.FileSize, profile.Executable.Sha256, new PeImageInfo(PeMachine.X86, profile.Executable.TimeDateStamp, profile.Executable.ImageBase, profile.Executable.SizeOfImage, 0x9D6B0, 0));
}

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public static class PeHeaderBuilder
{
    /// <summary>Builds a 0x400-byte PE32 header with the given identity fields.</summary>
    public static byte[] Build(uint timeDateStamp, uint imageBase, uint sizeOfImage, ushort machine = 0x14C, ushort magic = 0x10B)
    {
        var h = new byte[PeImageInfo.HeaderProbeSize];
        h[0] = (byte)'M';
        h[1] = (byte)'Z';
        const int pe = 0x80;
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(0x3C), pe);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(pe), 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(pe + 4), machine);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(pe + 8), timeDateStamp);
        var opt = pe + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(opt), magic);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(opt + 16), 0x9D6B0);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(opt + 28), imageBase);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(opt + 56), sizeOfImage);
        return h;
    }
}
