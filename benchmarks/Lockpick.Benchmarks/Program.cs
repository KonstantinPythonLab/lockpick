using System.Buffers.Binary;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Lockpick.Core.Adapters;
using Lockpick.Core.Events;
using Lockpick.Core.Fingerprinting;
using Lockpick.Core.Memory;
using Lockpick.Core.Processes;
using Lockpick.Core.Profiles;
using Lockpick.Core.State;
using Lockpick.GameAdapter.RE1;
using Lockpick.Platform.Windows;

// dotnet run -c Release --project benchmarks/Lockpick.Benchmarks -- --filter * [--job short]
BenchmarkSwitcher.FromAssembly(typeof(PollBenchmarks).Assembly).Run(args);

/// <summary>Cost of one RE1 poll (all profile fields + normalization) and of the pieces around it.</summary>
[MemoryDiagnoser]
public class PollBenchmarks
{
    private IGameSession _session = null!;
    private GameSnapshot _a = null!;
    private GameSnapshot _b = null!;
    private EventDetector _detector = null!;
    private long _sequence;

    [GlobalSetup]
    public void Setup()
    {
        var adapter = new Re1Adapter();
        var profile = adapter.Profiles[0];
        var memory = RegionMemory.ForProfile(profile);
        _session = adapter.Attach(new InMemoryProcess(memory), profile);
        _a = _session.Read(1, DateTimeOffset.UtcNow);
        _b = _a with { Camera = Field<int>.Valid(_a.Camera.Value + 1) };
        _detector = new EventDetector();
    }

    /// <summary>Adapter + normalization overhead with memory access excluded (in-process buffers).</summary>
    [Benchmark]
    public GameSnapshot Re1SessionRead() => _session.Read(++_sequence, DateTimeOffset.UtcNow);

    [Benchmark]
    public IReadOnlyList<GameEvent> DetectEvents() => _detector.Detect(_a, _b);
}

/// <summary>Real Win32 cross-process read cost (reads this process's own memory through a handle).</summary>
public class Win32ReadBenchmarks
{
    private Win32AttachedProcess _self = null!;
    private nint _buffer;

    [GlobalSetup]
    public void Setup()
    {
        _self = Win32AttachedProcess.Open(Environment.ProcessId);
        _buffer = Marshal.AllocHGlobal(64);
        Marshal.WriteInt32(_buffer, 140);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Marshal.FreeHGlobal(_buffer);
        _self.Dispose();
    }

    [Benchmark]
    public uint ReadProcessMemoryU32()
    {
        _self.TryReadU32((ulong)_buffer, out var value);
        return value;
    }

    [Benchmark]
    public bool HasExitedCheck() => _self.HasExited;
}

/// <summary>Fingerprinting a game-sized executable (1 MB), as done once per detected process.</summary>
public class FingerprintBenchmarks
{
    private string _path = null!;

    [GlobalSetup]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), "lockpick-bench-1mb.bin");
        var data = new byte[1_004_112];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(_path, data);
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_path);

    [Benchmark]
    public ExecutableFingerprint Sha256AndPeHeader() => FileFingerprintProvider.ComputeUncached(_path);
}

/// <summary>Contiguous in-process buffers laid out at the profile's addresses.</summary>
internal sealed class RegionMemory : IProcessMemory
{
    private readonly List<(ulong Start, byte[] Data)> _regions = [];

    public static RegionMemory ForProfile(VersionProfile profile)
    {
        var memory = new RegionMemory();
        var header = new byte[PeImageInfo.HeaderProbeSize];
        header[0] = (byte)'M';
        header[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x3C), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x80), 0x4550);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x84), 0x14C);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x88), profile.Executable.TimeDateStamp);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x98), 0x10B);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x98 + 56), profile.Executable.SizeOfImage);
        memory._regions.Add((profile.Executable.ImageBase, header));
        foreach (var anchor in profile.Anchors)
        {
            memory._regions.Add((anchor.Address, anchor.Bytes.ToArray()));
        }

        foreach (var field in profile.Fields)
        {
            memory._regions.Add((field.Address, new byte[8]));
        }

        // Plausible in-game values.
        memory.Set(profile, "maxHp", 140);
        memory.Set(profile, "hp", 140);
        memory.Set(profile, "room", 6);
        return memory;
    }

    public bool TryRead(ulong address, Span<byte> destination)
    {
        foreach (var (start, data) in _regions)
        {
            if (address >= start && address + (ulong)destination.Length <= start + (ulong)data.Length)
            {
                data.AsSpan((int)(address - start), destination.Length).CopyTo(destination);
                return true;
            }
        }

        return false;
    }

    private void Set(VersionProfile profile, string field, byte value)
    {
        var address = profile.FindField(field)!.Address;
        _regions.First(r => r.Start == address).Data[0] = value;
    }
}

internal sealed class InMemoryProcess(RegionMemory memory) : IAttachedProcess
{
    public int ProcessId => Environment.ProcessId;

    public bool HasExited => false;

    public ModuleImage MainModule => new(0x400000, 0x49B2000, null);

    public bool TryRead(ulong address, Span<byte> destination) => memory.TryRead(address, destination);

    public void Dispose()
    {
    }
}
