using Lockpick.Core.Events;
using Lockpick.Core.State;
using Lockpick.Tests.Fakes;

namespace Lockpick.Tests.Core;

public class EventDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_previous_snapshot_produces_no_events()
    {
        Assert.Empty(new EventDetector().Detect(null, MockGameAdapter.InRoom(1, T0, 1, 6, 0, 140)));
    }

    [Fact]
    public void Room_change_is_reported_without_a_camera_event()
    {
        var events = new EventDetector().Detect(
            MockGameAdapter.InRoom(1, T0, 1, 6, 5, 140),
            MockGameAdapter.InRoom(2, T0, 1, 5, 0, 140));

        var room = Assert.Single(events);
        var changed = Assert.IsType<RoomChanged>(room);
        Assert.Equal("106", changed.From.Id);
        Assert.Equal("105", changed.To.Id);
    }

    [Fact]
    public void Camera_change_within_a_room_is_reported()
    {
        var events = new EventDetector().Detect(
            MockGameAdapter.InRoom(1, T0, 1, 6, 0, 140),
            MockGameAdapter.InRoom(2, T0, 1, 6, 5, 140));

        var camera = Assert.IsType<CameraChanged>(Assert.Single(events));
        Assert.Equal(0, camera.From);
        Assert.Equal(5, camera.To);
    }

    [Fact]
    public void Health_drop_in_same_room_is_damage()
    {
        var events = new EventDetector().Detect(
            MockGameAdapter.InRoom(1, T0, 1, 6, 0, 140),
            MockGameAdapter.InRoom(2, T0, 1, 6, 0, 110));

        Assert.Contains(events, e => e is PlayerHealthChanged { From: 140, To: 110 });
        Assert.Contains(events, e => e is PlayerDamaged { Amount: 30, Remaining: 110 });
    }

    [Fact]
    public void Health_drop_across_rooms_is_not_called_damage()
    {
        var events = new EventDetector().Detect(
            MockGameAdapter.InRoom(1, T0, 1, 6, 0, 140),
            MockGameAdapter.InRoom(2, T0, 1, 5, 0, 100));

        Assert.Contains(events, e => e is PlayerHealthChanged);
        Assert.DoesNotContain(events, e => e is PlayerDamaged);
    }

    [Fact]
    public void Healing_is_a_health_change_but_not_damage()
    {
        var events = new EventDetector().Detect(
            MockGameAdapter.InRoom(1, T0, 1, 6, 0, 60),
            MockGameAdapter.InRoom(2, T0, 1, 6, 0, 140));

        Assert.IsType<PlayerHealthChanged>(Assert.Single(events));
    }

    [Fact]
    public void Camera_carried_over_from_previous_room_is_absorbed_until_play_resumes()
    {
        // Live RE1 door 106 -> 105: room id flips first, camera id follows ~4.7 s later while the timer is frozen.
        static GameSnapshot At(double wallSeconds, int room, int cam, int frames) =>
            MockGameAdapter.InRoom(0, T0.AddSeconds(wallSeconds), 1, room, cam, 140) with { GameTime = Field<TimeSpan>.Valid(TimeSpan.FromSeconds(frames / 30.0)) };

        var events = EventDetector.DetectAll(
        [
            At(0.0, 6, 5, 1067),
            At(0.1, 5, 5, 1068), // room changed, camera still the old room's
            At(4.8, 5, 0, 1068), // camera assigned during the door animation (timer frozen)
            At(4.9, 5, 0, 1069), // play resumes
            At(9.3, 5, 1, 1200), // real camera cut while walking
        ]);

        Assert.Collection(
            events,
            e => Assert.IsType<RoomChanged>(e),
            e => Assert.Equal((0, 1), (((CameraChanged)e).From, ((CameraChanged)e).To)));
    }

    private static GameSnapshot Timed(double seconds, double frames, bool inGame = true) =>
        (inGame ? MockGameAdapter.InRoom(0, T0.AddSeconds(seconds), 1, 6, 0, 140) : MockGameAdapter.Unreadable(0, T0.AddSeconds(seconds)))
        with { GameTime = inGame ? Field<TimeSpan>.Valid(TimeSpan.FromSeconds(frames / 30.0)) : Field<TimeSpan>.Unavailable };

    [Fact]
    public void Attaching_to_a_game_in_progress_is_not_a_load()
    {
        Assert.Empty(EventDetector.DetectAll([Timed(0, 9000), Timed(1, 9030), Timed(2, 9060)]));
    }

    [Fact]
    public void Menus_freezing_the_timer_are_not_a_load()
    {
        Assert.Empty(EventDetector.DetectAll([Timed(0, 9000), Timed(5, 9000), Timed(30, 9000), Timed(31, 9030)]));
    }

    [Fact]
    public void Starting_from_the_title_after_attach_is_detected()
    {
        var newGame = EventDetector.DetectAll([Timed(0, 0, inGame: false), Timed(10, 0)]);
        Assert.True(Assert.IsType<GameLoaded>(Assert.Single(newGame)).IsNewGame);

        var loaded = EventDetector.DetectAll([Timed(0, 0, inGame: false), Timed(10, 54000)]);
        Assert.False(Assert.IsType<GameLoaded>(Assert.Single(loaded)).IsNewGame);
    }

    [Fact]
    public void Loading_a_later_save_is_detected_from_a_forward_jump()
    {
        // 5 s of real time cannot advance the timer by 10 minutes.
        var events = EventDetector.DetectAll([Timed(0, 9000), Timed(1, 0, inGame: false), Timed(5, 9000 + 18000)]);
        var loaded = Assert.IsType<GameLoaded>(Assert.Single(events));
        Assert.False(loaded.IsNewGame);
    }

    [Fact]
    public void Load_screen_showing_the_previous_timer_is_not_a_load()
    {
        var events = EventDetector.DetectAll([Timed(0, 25774), Timed(1, 0, inGame: false), Timed(6, 25774), Timed(13, 25171)]);
        var loaded = Assert.IsType<GameLoaded>(Assert.Single(events));
        Assert.Equal(TimeSpan.FromSeconds(25171 / 30.0), loaded.GameTime);
    }

    [Theory]
    [InlineData(FieldStatus.Unavailable)]
    [InlineData(FieldStatus.Stale)]
    [InlineData(FieldStatus.Unsupported)]
    public void Non_valid_fields_never_produce_events(FieldStatus status)
    {
        var before = MockGameAdapter.InRoom(1, T0, 1, 6, 0, 140);
        var after = MockGameAdapter.InRoom(2, T0, 1, 5, 3, 10) with
        {
            Location = new Field<RoomLocation>(status, new RoomLocation(1, 5)),
            Camera = new Field<int>(status, 3),
            Health = new Field<Health>(status, new Health(10, 140)),
        };

        Assert.Empty(new EventDetector().Detect(before, after));
    }
}
