using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockpick.Core.Insight;

namespace Lockpick.App.ViewModels;

/// <summary>An ask-mode question: what changed, and the player's answer (null when skipped).</summary>
public sealed partial class QuestionViewModel(PendingQuestion question, string? pauseProblem) : ObservableObject
{
    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingQuestion Question { get; } = question;

    public string Where { get; } = string.Create(CultureInfo.InvariantCulture,
        $"Room {question.Room ?? "?"}{(question.GameTime is { } t ? $" · game time {t:h\\:mm\\:ss}" : string.Empty)} · {question.At.ToLocalTime():HH:mm:ss}");

    public IReadOnlyList<string> Changes { get; } = [.. question.Changes.Select(c => c.Description)];

    /// <summary>Shown when the game couldn't be frozen (e.g. it runs as administrator).</summary>
    public string? PauseProblem { get; } = pauseProblem is null ? null : $"The game couldn't be paused ({pauseProblem}); it keeps running.";

    [ObservableProperty]
    public partial string Answer { get; set; } = string.Empty;

    /// <summary>Completes with the answer, or null when skipped or closed.</summary>
    public Task<string?> Result => _result.Task;

    [RelayCommand]
    private void Save() => _result.TrySetResult(string.IsNullOrWhiteSpace(Answer) ? null : Answer.Trim());

    [RelayCommand]
    public void Skip() => _result.TrySetResult(null);
}
