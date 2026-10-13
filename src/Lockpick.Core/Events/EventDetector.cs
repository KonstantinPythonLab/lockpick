using Lockpick.Core.State;

namespace Lockpick.Core.Events;

/// <summary>
/// Derives typed events by diffing consecutive snapshots. Only transitions between two
/// <see cref="FieldStatus.Valid"/> values produce events, so unsupported, unavailable or stale
/// data never manufactures an event. One instance per attachment (it tracks room-load settling).
/// </summary>
public sealed class EventDetector
{
    // After a room change the game keeps the old room's camera id until the new room finishes
    // loading (observed live in RE1: ~4.7 s with the game timer frozen). Camera changes are only
    // reported once the game timer has advanced again, i.e. play has resumed in the new room.
    private bool _roomLoading;
    private TimeSpan? _roomLoadTime;

    // New game / load detection works on the in-game timer, which only runs during play: a new game resets
    // it to ~0 and loading a save restores the saved value. The last valid value is kept across title-screen
    // gaps so the load screen (which still shows the previous game's timer) does not count as a load.
    private TimeSpan? _lastGameTime;
    private DateTimeOffset _lastGameTimeAt;

    /// <summary>A timer value below this when a session (re)starts means a new game rather than a save.</summary>
    public static readonly TimeSpan NewGameThreshold = TimeSpan.FromSeconds(2);

    /// <summary>Extra slack when deciding that the timer advanced faster than real time (i.e. a load).</summary>
    public static readonly TimeSpan ForwardJumpTolerance = TimeSpan.FromSeconds(2);

    public void Reset()
    {
        _roomLoading = false;
        _roomLoadTime = null;
        _lastGameTime = null;
        _lastGameTimeAt = default;
    }

    public IReadOnlyList<GameEvent> Detect(GameSnapshot? previous, GameSnapshot current)
    {
        if (previous is null)
        {
            // First snapshot after attaching: remember the timer, but a game already in progress is not a load.
            RememberGameTime(current);
            return [];
        }

        var events = new List<GameEvent>(2);
        var at = current.CapturedAt;

        if (DetectLoad(current) is { } loaded)
        {
            events.Add(loaded);
        }

        bool sameRoom = previous.Location.TryGet(out var fromRoom)
            && current.Location.TryGet(out var toRoom)
            && fromRoom == toRoom;

        if (previous.Location.TryGet(out fromRoom) && current.Location.TryGet(out toRoom) && fromRoom != toRoom)
        {
            events.Add(new RoomChanged(at, fromRoom, toRoom));
            _roomLoading = current.GameTime.Status != FieldStatus.Unsupported;
            _roomLoadTime = current.GameTime.IsValid ? current.GameTime.Value : null;
        }
        else if (sameRoom
            && !_roomLoading
            && previous.Camera.TryGet(out var fromCam)
            && current.Camera.TryGet(out var toCam)
            && fromCam != toCam)
        {
            events.Add(new CameraChanged(at, current.Location.Value, fromCam, toCam));
        }

        UpdateRoomLoading(current);

        if (previous.Health.TryGet(out var fromHp) && current.Health.TryGet(out var toHp) && fromHp.Current != toHp.Current)
        {
            events.Add(new PlayerHealthChanged(at, fromHp.Current, toHp.Current));

            // A drop across a room transition is more likely a load than damage, so require the same room.
            if (toHp.Current < fromHp.Current && sameRoom)
            {
                events.Add(new PlayerDamaged(at, fromHp.Current - toHp.Current, toHp.Current));
            }
        }

        return events;
    }

    /// <summary>Convenience for tests and tools: runs a sequence through a fresh detector.</summary>
    public static IReadOnlyList<GameEvent> DetectAll(IEnumerable<GameSnapshot> snapshots)
    {
        var detector = new EventDetector();
        var events = new List<GameEvent>();
        GameSnapshot? previous = null;
        foreach (var snapshot in snapshots)
        {
            events.AddRange(detector.Detect(previous, snapshot));
            previous = snapshot;
        }

        return events;
    }

    private GameLoaded? DetectLoad(GameSnapshot current)
    {
        if (!current.GameTime.TryGet(out var time))
        {
            return null;
        }

        var last = _lastGameTime;
        var lastAt = _lastGameTimeAt;
        RememberGameTime(current);

        bool restarted;
        if (last is null)
        {
            // Attached before any game was running (boot/title); the first in-game time starts a session.
            restarted = true;
        }
        else if (time < last.Value)
        {
            restarted = true;
        }
        else
        {
            // The timer can't run faster than real time; a bigger forward jump is a later save being loaded.
            var wall = current.CapturedAt - lastAt;
            restarted = time - last.Value > wall + ForwardJumpTolerance;
        }

        if (!restarted)
        {
            return null;
        }

        // A new room-load cycle starts with the new session.
        _roomLoading = false;
        return new GameLoaded(current.CapturedAt, time, IsNewGame: time < NewGameThreshold);
    }

    private void RememberGameTime(GameSnapshot snapshot)
    {
        if (snapshot.GameTime.TryGet(out var time))
        {
            _lastGameTime = time;
            _lastGameTimeAt = snapshot.CapturedAt;
        }
    }

    private void UpdateRoomLoading(GameSnapshot current)
    {
        if (!_roomLoading || !current.GameTime.TryGet(out var time))
        {
            return;
        }

        if (_roomLoadTime is null)
        {
            _roomLoadTime = time;
        }
        else if (time > _roomLoadTime)
        {
            _roomLoading = false;
        }
    }
}
