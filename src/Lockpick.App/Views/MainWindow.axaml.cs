using Avalonia.Controls;
using Lockpick.App.ViewModels;

namespace Lockpick.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Debug.AskPlayer = QuestionWindow.AskAsync;
            }
        };
    }
}
