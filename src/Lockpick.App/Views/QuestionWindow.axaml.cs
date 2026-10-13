using Avalonia;
using Avalonia.Controls;
using Lockpick.App.ViewModels;

namespace Lockpick.App.Views;

/// <summary>Ask-mode pop-up. Opens on the primary screen (where the game usually is), on top, and closes with its answer.</summary>
public partial class QuestionWindow : Window
{
    public QuestionWindow() => InitializeComponent();

    /// <summary>Shows the question and completes when it is answered, skipped or the window is closed.</summary>
    public static async Task<string?> AskAsync(QuestionViewModel question)
    {
        var window = new QuestionWindow { DataContext = question };
        window.Closing += (_, _) => question.Skip();
        window.Opened += (_, _) =>
        {
            if (window.Screens.Primary is { } screen)
            {
                var area = screen.WorkingArea;
                var size = PixelSize.FromSize(window.ClientSize, screen.Scaling);
                window.Position = new PixelPoint(area.X + ((area.Width - size.Width) / 2), area.Y + ((area.Height - size.Height) / 3));
            }

            window.Activate();
            window.FindControl<TextBox>("AnswerBox")?.Focus();
        };
        window.Show();
        try
        {
            return await question.Result.ConfigureAwait(true);
        }
        finally
        {
            window.Close();
        }
    }
}
