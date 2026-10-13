using Lockpick.Core.Profiles;
using Lockpick.Core.Recording;
using Lockpick.Core.State;
using Lockpick.GameAdapter.RE1;

namespace Lockpick.Tests.Core;

public class RegionLogTests
{
    /// <summary>
    /// Golden file shared with tools/bounties/test_bounties.py, pinning the binary format across C# and Python.
    /// Regenerate with LOCKPICK_UPDATE_GOLDEN=1 after an intentional format change.
    /// </summary>
    private static readonly string GoldenPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "region-log-sample.bin");

    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Round_trips_keyframes_and_deltas()
    {
        var regions = new List<MemoryRegionSpec> { new() { Name = "a", Address = 0x1000, Length = 64 }, new() { Name = "b", Address = 0x2000, Length = 16 } };
        var a = new byte[64];
        var b = new byte[16];
        var stream = new MemoryStream();
        var states = new List<(byte[], byte[])>();
        using (var writer = new RegionLogWriter(stream, regions))
        {
            for (var poll = 0; poll < 5; poll++)
            {
                a[poll * 7 % 64] = (byte)(poll + 1);
                if (poll == 3)
                {
                    b[15] = 0xEE;
                }

                writer.Append(T0.AddMilliseconds(33 * poll), [new RegionData("a", 0x1000, (byte[])a.Clone()), new RegionData("b", 0x2000, (byte[])b.Clone())]);
                states.Add(((byte[])a.Clone(), (byte[])b.Clone()));
            }

            Assert.False(writer.Append(T0.AddSeconds(1), [new RegionData("a", 0x1000, (byte[])a.Clone()), new RegionData("b", 0x2000, (byte[])b.Clone())]));
            Assert.Equal(1, writer.Keyframes);
        }

        var (read, records) = RegionLogReader.Open(new MemoryStream(stream.ToArray()));
        Assert.Equal(["a", "b"], read.Select(r => r.Name));
        var replayed = records.ToList();
        Assert.Equal(5, replayed.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(states[i].Item1, replayed[i].State[0]);
            Assert.Equal(states[i].Item2, replayed[i].State[1]);
        }
    }

    [Fact]
    public void A_new_keyframe_is_written_every_interval()
    {
        var regions = new List<MemoryRegionSpec> { new() { Name = "a", Address = 0, Length = 8 } };
        using var writer = new RegionLogWriter(new MemoryStream(), regions);
        var data = new byte[8];
        for (var second = 0; second <= 130; second += 10)
        {
            data[0] = (byte)second;
            writer.Append(T0.AddSeconds(second), [new RegionData("a", 0, (byte[])data.Clone())]);
        }

        Assert.Equal(3, writer.Keyframes); // t = 0, 60, 120
    }

    [Fact]
    public void Golden_sample_matches_the_committed_file()
    {
        var bytes = BuildGoldenSample();
        if (Environment.GetEnvironmentVariable("LOCKPICK_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllBytes(GoldenPath, bytes);
        }

        Assert.Equal(File.ReadAllBytes(GoldenPath), bytes);
    }

    /// <summary>
    /// Room 106 loads with a leftover slot from the previous room (type 37, cleared to -1 within a frame, as seen live);
    /// once the room has settled a dog (type 2) is shot down, a door unlocks and an item moves in the item box.
    /// </summary>
    private static byte[] BuildGoldenSample()
    {
        var profile = new Re1Adapter().Profiles[0];
        var game = profile.CaptureRegions.Single(r => r.Name == "gameState");
        var system = profile.CaptureRegions.Single(r => r.Name == "system");
        var g = new byte[game.Length];
        var sys = new byte[system.Length];

        void Put(string field, long value)
        {
            var spec = profile.FindField(field)!;
            var (buffer, start) = spec.Address >= system.Address ? (sys, system.Address) : (g, game.Address);
            var offset = (int)(spec.Address - start);
            var size = Lockpick.Core.Memory.ValueTypeExtensions.SizeOf(spec.Type);
            for (var i = 0; i < size; i++)
            {
                buffer[offset + i] = (byte)(value >> (8 * i));
            }
        }

        Put("room", 0x06);
        Put("hp", 96);
        Put("maxHp", 96);
        Put("character", 1);
        Put("mainMenu", 0);
        Put("gameTimer", 1000);
        Put("enemy0Flags", 1);
        Put("enemy0Type", 2);
        Put("enemy0Hp", 30);
        Put("enemy1Flags", 1);
        Put("enemy1Type", 37);
        Put("enemy1Hp", 38);
        Put("box5", 0x0F0B);

        var stream = new MemoryStream();
        using (var writer = new RegionLogWriter(stream, profile.CaptureRegions))
        {
            void Poll(int ms) => writer.Append(T0.AddMilliseconds(ms), [new RegionData("gameState", game.Address, (byte[])g.Clone()), new RegionData("system", system.Address, (byte[])sys.Clone())]);
            Poll(0);
            Put("gameTimer", 1001);
            Put("enemy1Hp", -1);
            Poll(33);
            Put("gameTimer", 1040);
            Put("enemy0Hp", 12);
            Poll(1333);
            Put("gameTimer", 1041);
            Put("enemy0Hp", 0);
            Put("flagLocks0", 0x10);
            Poll(1366);
            Put("gameTimer", 1042);
            Put("box5", 0);
            Poll(1400);
        }

        return stream.ToArray();
    }
}
