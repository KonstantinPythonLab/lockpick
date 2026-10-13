using Lockpick.Core.State;

namespace Lockpick.Core.Insight;

/// <summary>A change in the game's state that the adapter has no explanation for (e.g. an unknown story flag).</summary>
/// <param name="Key">Stable identity, so the same change is asked about only once.</param>
/// <param name="Description">What changed, in words ("story flag B0 #7 set").</param>
public sealed record UnexplainedChange(string Key, string Description);

/// <summary>Finds changes between two consecutive snapshots that the game's known data doesn't account for.</summary>
public interface IChangeExplainer
{
    IReadOnlyList<UnexplainedChange> Unexplained(GameSnapshot previous, GameSnapshot current);
}

/// <summary>A group of unexplained changes to ask the player about, with where and when the first one happened.</summary>
public sealed record PendingQuestion(IReadOnlyList<UnexplainedChange> Changes, string? Room, TimeSpan? GameTime, DateTimeOffset At);

/// <summary>
/// Collects unexplained changes and decides when to ask about them. Story events set their flags as a cutscene starts,
/// so the question waits until the changes have stopped for <see cref="QuietPeriod"/> and the game is playing again
/// (in a game session with the in-game timer running). Each change is asked about once per capture.
/// </summary>
public sealed class QuestionScheduler(IChangeExplainer explainer)
{
    private const int MaxChangesPerQuestion = 12;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<UnexplainedChange> _pending = [];
    private GameSnapshot? _previous;
    private GameSnapshot? _first;
    private DateTimeOffset _lastChangeAt;

    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Feeds the next poll; returns a question when one is ready to be asked.</summary>
    public PendingQuestion? Feed(GameSnapshot current)
    {
        var previous = _previous;
        _previous = current;
        if (previous is null)
        {
            return null;
        }

        foreach (var change in explainer.Unexplained(previous, current))
        {
            if (_seen.Add(change.Key) && _pending.Count < MaxChangesPerQuestion)
            {
                _first ??= current;
                _pending.Add(change);
                _lastChangeAt = current.CapturedAt;
            }
        }

        var playing = current.InGameplay is { HasValue: true, Value: true }
            && current.GameTime.TryGet(out var now) && previous.GameTime.TryGet(out var before) && now > before;
        if (_pending.Count == 0 || !playing || current.CapturedAt - _lastChangeAt < QuietPeriod)
        {
            return null;
        }

        var first = _first!;
        var question = new PendingQuestion(
            [.. _pending],
            first.Location.TryGet(out var room) ? room.Id : null,
            first.GameTime.TryGet(out var time) ? time : null,
            first.CapturedAt);
        _pending.Clear();
        _first = null;
        return question;
    }
}
