using Lockpick.Core.Events;
using Lockpick.Core.Recording;
using Lockpick.Core.State;

namespace Lockpick.Tests.Core;

public class RecordingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 3, 49, 23, TimeSpan.Zero);

    [Fact]
    public void Snapshots_round_trip_through_a_recording()
    {
        var snapshot = new GameSnapshot
        {
            Sequence = 7,
            CapturedAt = T0.AddMilliseconds(500),
            InGameplay = Field<bool>.Valid(true),
            Character = Field<PlayableCharacter>.Valid(PlayableCharacter.Jill),
            Health = Field<Health>.Valid(new Health(96, 96)),
            Location = Field<RoomLocation>.Valid(new RoomLocation(1, 6)),
            Camera = new Field<int>(FieldStatus.Stale, 3),
            Position = Field<Position>.Valid(new Position(1, -2, 3)),
            GameTime = Field<TimeSpan>.Valid(TimeSpan.FromSeconds(12.5)),
            RawFields = new Dictionary<string, Field<long>> { ["hp"] = Field<long>.Valid(96) },
        };

        var writer = new StringWriter();
        using (var recorder = new SessionRecorder(writer, "re1", "re1-gog-usa-hotfix4", T0))
        {
            recorder.Append(snapshot);
        }

        var (header, frames) = SessionRecording.Read(new StringReader(writer.ToString()));
        Assert.Equal("re1-gog-usa-hotfix4", header.ProfileId);
        var frame = Assert.Single(frames);
        Assert.Equal(500, frame.OffsetMs);
        Assert.True(frame.Snapshot.HasSameStateAs(snapshot));
        Assert.Equal(96, frame.Snapshot.RawFields["hp"].Value);
        Assert.Equal(FieldStatus.Stale, frame.Snapshot.Camera.Status);
    }

    [Fact]
    public void Rejects_unknown_formats()
    {
        Assert.Throws<InvalidDataException>(() => SessionRecording.Read(new StringReader("""{"format":"other","version":1,"gameId":"x","recordedAt":"2026-01-01T00:00:00Z"}""")));
        Assert.Throws<InvalidDataException>(() => SessionRecording.Read(new StringReader(string.Empty)));
    }

    /// <summary>
    /// Fixture converted from a live GOG trace (2026-10-12, tools/fixtures/trace_to_fixture.py; raw trace
    /// beside it): new Chris game, main hall → dining room → corridor, zombie attack until death, return
    /// to title, then a second New Game.
    /// </summary>
    [Fact]
    public void Live_fixture_plays_back_into_reliable_events()
    {
        var (header, frames) = SessionRecording.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "re1-live-chris-death-and-restart.jsonl"));
        Assert.Equal("re1-gog-usa-hotfix4", header.ProfileId);

        var events = SessionRecording.DetectEvents(frames);
        var described = events.Select(e => e.Describe()).ToList();

        // Exactly the two doors that were walked through; the restart after death is not a room change.
        Assert.Equal(["Room 106 → 105", "Room 105 → 104"], events.OfType<RoomChanged>().Select(e => e.Describe()));

        // The camera id carried over from room 106 (5) and assigned while the door animation ran (→ 0)
        // must not be reported.
        Assert.DoesNotContain("Camera 5 → 0 in room 105", described);

        // 13 zombie bites of 10 HP each. The fatal 10 → 0 hit ends the session (player dead), and the
        // dead game's leftovers (room 11B, HP -10) during the next New Game never surface as events.
        var damage = events.OfType<PlayerDamaged>().ToList();
        Assert.Equal(13, damage.Count);
        Assert.All(damage, d => Assert.Equal(10, d.Amount));
        Assert.Equal(10, damage[^1].Remaining);
        Assert.DoesNotContain(described, d => d.Contains("11B", StringComparison.Ordinal) || d.Contains("-10", StringComparison.Ordinal));

        // After the restart, camera cuts in the main hall are tracked again.
        Assert.Equal("Camera 1 → 5 in room 106", described[^1]);
    }
}
