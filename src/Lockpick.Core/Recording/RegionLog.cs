using System.Text;
using Lockpick.Core.Profiles;
using Lockpick.Core.State;

namespace Lockpick.Core.Recording;

/// <summary>
/// Binary log of raw memory regions (memory-N.bin in a capture). It stores a full copy of every region (keyframe)
/// at the start and every <see cref="KeyframeInterval"/>, and only the changed byte runs in between, so a capture keeps
/// the complete game state at 30 Hz in little space and can be re-analyzed later for values no field describes yet.
/// </summary>
/// <remarks>
/// Format (little-endian):
/// <code>
/// header : "LPKMEM" u8 version(=1) u16 regionCount { u8 nameLength, name(UTF-8), u64 address, u32 length }*
/// record : u8 kind, i64 unixMilliseconds, then
///          kind 0 (keyframe): { u8 present, [length bytes if present] } per region
///          kind 1 (delta)   : u16 runCount, { u16 regionIndex, u32 offset, u16 length, bytes }*
/// </code>
/// A region that could not be read for a poll is left unchanged (delta) or marked absent (keyframe).
/// </remarks>
public sealed class RegionLogWriter : IDisposable
{
    public const string Magic = "LPKMEM";
    public const byte Version = 1;
    public static readonly TimeSpan KeyframeInterval = TimeSpan.FromSeconds(60);

    /// <summary>Changed bytes closer than this are merged into one run (fewer run headers).</summary>
    private const int MergeGap = 8;

    private readonly BinaryWriter _writer;
    private readonly MemoryRegionSpec[] _regions;
    private readonly byte[]?[] _previous;
    private DateTimeOffset _lastKeyframe = DateTimeOffset.MinValue;

    public RegionLogWriter(Stream stream, IReadOnlyList<MemoryRegionSpec> regions)
    {
        _writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        _regions = [.. regions];
        _previous = new byte[]?[_regions.Length];
        _writer.Write(Encoding.ASCII.GetBytes(Magic));
        _writer.Write(Version);
        _writer.Write((ushort)_regions.Length);
        foreach (var region in _regions)
        {
            var name = Encoding.UTF8.GetBytes(region.Name);
            _writer.Write((byte)name.Length);
            _writer.Write(name);
            _writer.Write(region.Address);
            _writer.Write(region.Length);
        }
    }

    public long Records { get; private set; }

    public long Keyframes { get; private set; }

    /// <summary>Appends one poll. Returns false when nothing changed (no record written).</summary>
    public bool Append(DateTimeOffset at, IReadOnlyList<RegionData> data)
    {
        var current = new byte[]?[_regions.Length];
        for (var i = 0; i < _regions.Length; i++)
        {
            foreach (var d in data)
            {
                if (string.Equals(d.Name, _regions[i].Name, StringComparison.Ordinal) && d.Data.Length == _regions[i].Length)
                {
                    current[i] = d.Data;
                }
            }
        }

        var newlyReadable = false;
        for (var i = 0; i < _regions.Length; i++)
        {
            newlyReadable |= current[i] is not null && _previous[i] is null;
        }

        if (newlyReadable || at - _lastKeyframe >= KeyframeInterval)
        {
            WriteKeyframe(at, current);
            return true;
        }

        return WriteDelta(at, current);
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();

    private void WriteKeyframe(DateTimeOffset at, byte[]?[] current)
    {
        _writer.Write((byte)0);
        _writer.Write(at.ToUnixTimeMilliseconds());
        for (var i = 0; i < _regions.Length; i++)
        {
            var bytes = current[i] ?? _previous[i];
            _writer.Write((byte)(bytes is null ? 0 : 1));
            if (bytes is not null)
            {
                _writer.Write(bytes);
                _previous[i] = (byte[])bytes.Clone();
            }
        }

        _lastKeyframe = at;
        Records++;
        Keyframes++;
    }

    private bool WriteDelta(DateTimeOffset at, byte[]?[] current)
    {
        var runs = new List<(int Region, int Offset, int Length)>();
        for (var i = 0; i < _regions.Length; i++)
        {
            if (current[i] is not { } now || _previous[i] is not { } before)
            {
                continue;
            }

            var start = -1;
            var lastDiff = -1;
            for (var b = 0; b < now.Length; b++)
            {
                if (now[b] == before[b])
                {
                    continue;
                }

                if (start >= 0 && b - lastDiff > MergeGap)
                {
                    AddRuns(runs, i, start, lastDiff);
                    start = -1;
                }

                if (start < 0)
                {
                    start = b;
                }

                lastDiff = b;
            }

            if (start >= 0)
            {
                AddRuns(runs, i, start, lastDiff);
            }
        }

        if (runs.Count == 0)
        {
            return false;
        }

        _writer.Write((byte)1);
        _writer.Write(at.ToUnixTimeMilliseconds());
        _writer.Write((ushort)Math.Min(runs.Count, ushort.MaxValue));
        foreach (var (region, offset, length) in runs.Take(ushort.MaxValue))
        {
            _writer.Write((ushort)region);
            _writer.Write((uint)offset);
            _writer.Write((ushort)length);
            _writer.Write(current[region]!, offset, length);
            Buffer.BlockCopy(current[region]!, offset, _previous[region]!, offset, length);
        }

        Records++;
        return true;
    }

    private static void AddRuns(List<(int, int, int)> runs, int region, int first, int last)
    {
        // Split long runs so each length fits in a u16.
        for (var offset = first; offset <= last; offset += ushort.MaxValue)
        {
            runs.Add((region, offset, Math.Min(ushort.MaxValue, last - offset + 1)));
        }
    }
}

/// <summary>Reads a <see cref="RegionLogWriter"/> log back into full region states (used by tests and tools).</summary>
public static class RegionLogReader
{
    public sealed record Region(string Name, ulong Address, uint Length);

    public static (IReadOnlyList<Region> Regions, IEnumerable<(DateTimeOffset At, byte[][] State)> Records) Open(Stream stream)
    {
        var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(RegionLogWriter.Magic.Length));
        if (magic != RegionLogWriter.Magic || reader.ReadByte() != RegionLogWriter.Version)
        {
            throw new InvalidDataException("Not a Lockpick region log.");
        }

        var regions = new List<Region>();
        for (int i = 0, count = reader.ReadUInt16(); i < count; i++)
        {
            var name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadByte()));
            regions.Add(new Region(name, reader.ReadUInt64(), reader.ReadUInt32()));
        }

        return (regions, ReadRecords(reader, regions));
    }

    private static IEnumerable<(DateTimeOffset, byte[][])> ReadRecords(BinaryReader reader, List<Region> regions)
    {
        var state = regions.Select(r => new byte[r.Length]).ToArray();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var kind = reader.ReadByte();
            var at = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            if (kind == 0)
            {
                for (var i = 0; i < regions.Count; i++)
                {
                    if (reader.ReadByte() == 1)
                    {
                        state[i] = reader.ReadBytes((int)regions[i].Length);
                    }
                }
            }
            else
            {
                for (int r = 0, runs = reader.ReadUInt16(); r < runs; r++)
                {
                    var region = reader.ReadUInt16();
                    var offset = (int)reader.ReadUInt32();
                    var bytes = reader.ReadBytes(reader.ReadUInt16());
                    Buffer.BlockCopy(bytes, 0, state[region], offset, bytes.Length);
                }
            }

            yield return (at, state.Select(s => (byte[])s.Clone()).ToArray());
        }
    }
}
