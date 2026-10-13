using Lockpick.Core.State;

namespace Lockpick.Core.Insight;

/// <summary>A change in the game's state that the adapter has no explanation for (e.g. an unknown story flag).</summary>
/// <param name="Key">Stable identity, so the same change is asked about only once.</param>
/// <param name="Description">What changed, in words ("story flag B0 #7 set").</param>
public sealed record UnexplainedChange(string Key, string Description);

/// <summary>What changed between two polls: changes the game's data can't explain, and notable ones it can.</summary>
/// <param name="Known">Explained events worth showing as context ("picked up Emblem", "entered room 105").</param>
public sealed record ChangeReport(IReadOnlyList<UnexplainedChange> Unexplained, IReadOnlyList<string> Known)
{
    public static readonly ChangeReport Empty = new([], []);
}

/// <summary>Explains the changes between two consecutive snapshots using the game's known data.</summary>
public interface IChangeExplainer
{
    ChangeReport Explain(GameSnapshot previous, GameSnapshot current);
}

/// <summary>
/// A group of unexplained changes to ask the player about, with where and when the first one happened and the known
/// events around it (so the player can tell which moment is meant).
/// </summary>
public sealed record PendingQuestion(IReadOnlyList<UnexplainedChange> Changes, string? Room, TimeSpan? GameTime, DateTimeOffset At)
{
    /// <summary>Known events shortly before and after the first change, oldest first, e.g. "1:40 picked up Emblem".</summary>
    public IReadOnlyList<string> Context { get; init; } = [];
}

/// <summary>
/// Collects unexplained changes and decides when to ask about them. Story events set their flags as a cutscene starts,
/// so the question waits until the changes have stopped for <see cref="QuietPeriod"/> and the game is playing again
/// (in a game session with the in-game timer running). Each change is asked about once per capture.
/// </summary>
public sealed class QuestionScheduler(IChangeExplainer explainer)
{
    private const int MaxChangesPerQuestion = 12;
    private const int MaxRecent = 12;
    private static readonly TimeSpan ContextBefore = TimeSpan.FromSeconds(30);

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<UnexplainedChange> _pending = [];
    private readonly List<(DateTimeOffset At, string Text)> _recent = [];
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

        var report = explainer.Explain(previous, current);
        foreach (var known in report.Known)
        {
            _recent.Add((current.CapturedAt, $"{Clock(current)}{known}"));
            if (_recent.Count > MaxRecent)
            {
                _recent.RemoveAt(0);
            }
        }

        foreach (var change in report.Unexplained)
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
            first.CapturedAt)
        {
            Context = [.. _recent.Where(r => r.At >= first.CapturedAt - ContextBefore).Select(r => r.Text)],
        };
        _pending.Clear();
        _first = null;
        return question;
    }

    private static string Clock(GameSnapshot snapshot) =>
        snapshot.GameTime.TryGet(out var t) ? $"{(int)t.TotalMinutes}:{t.Seconds:00} " : string.Empty;
}
