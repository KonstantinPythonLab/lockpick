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

    [Fact]
    public void Chris_fixture_detects_the_new_game_after_death()
    {
        var events = Events("re1-live-chris-death-and-restart");
        Assert.Equal(["New game started"], events.OfType<GameLoaded>().Select(e => e.Describe()));
    }

    /// <summary>
    /// Jill session (2026-10-12): bitten to death in the corridor, New Game, a long route through the mansion
    /// including 2F, healed with a first aid spray, then back to the title and a save loaded.
    /// </summary>
    [Fact]
    public void Jill_fixture_plays_back_into_reliable_events()
    {
        var events = Events("re1-live-jill-death-load");

        Assert.Equal(
            ["New game started", "Save loaded (in-game time 00:13:59)"],
            events.OfType<GameLoaded>().Select(e => e.Describe()));

        // The load is detected when the save's time appears, not on the load-file screen (which still shows the
        // previous game's timer).
        var load = events.OfType<GameLoaded>().Single(e => !e.IsNewGame);
        Assert.Equal(TimeSpan.FromSeconds(0x6253 / 30.0), load.GameTime);

        Assert.Equal(
            ["105 → 104", "106 → 105", "105 → 104", "104 → 105", "105 → 106", "106 → 203", "203 → 106", "106 → 107",
             "107 → 108", "108 → 109", "109 → 113", "113 → 109", "109 → 115", "115 → 116", "116 → 115", "115 → 109",
             "109 → 108", "108 → 107", "107 → 106"],
            events.OfType<RoomChanged>().Select(e => $"{e.From} → {e.To}"));

        // 9 + 5 bites of 10 from zombies and one 12-point hit; the fatal bite and the first aid spray (46 → 96)
        // are not damage.
        var damage = events.OfType<PlayerDamaged>().Select(d => d.Amount).ToList();
        Assert.Equal(15, damage.Count);
        Assert.Equal(14, damage.Count(d => d == 10));
        Assert.Contains(12, damage);
        Assert.Contains(events, e => e is PlayerHealthChanged { From: 46, To: 96 });
    }

    private static IReadOnlyList<GameEvent> Events(string fixture)
    {
        var (_, frames) = SessionRecording.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".jsonl"));
        return SessionRecording.DetectEvents(frames);
    }
}
