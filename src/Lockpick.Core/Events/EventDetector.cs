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

    public void Reset()
    {
        _roomLoading = false;
        _roomLoadTime = null;
    }

    public IReadOnlyList<GameEvent> Detect(GameSnapshot? previous, GameSnapshot current)
    {
        if (previous is null)
        {
            return [];
        }

        var events = new List<GameEvent>(2);
        var at = current.CapturedAt;

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
