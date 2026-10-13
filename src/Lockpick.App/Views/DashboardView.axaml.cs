using Avalonia;
using Avalonia.Controls;
using Lockpick.App.Controls;

namespace Lockpick.App.Views;

public partial class DashboardView : UserControl
{
    /// <summary>Below this content width the cards stack in one column (portrait / side-by-side with the game).</summary>
    public const double SingleColumnBelow = 780;

    private bool? _compact;

    public DashboardView()
    {
        InitializeComponent();
        ApplyLayout(compact: false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            ApplyLayout(Responsive.IsCompact(Bounds.Width, SingleColumnBelow));
        }
    }

    private void ApplyLayout(bool compact)
    {
        if (_compact == compact)
        {
            return;
        }

        _compact = compact;
        if (compact)
        {
            Root.ColumnDefinitions = new ColumnDefinitions("*");
            Root.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*");
            Place(ConnectionCard, 0, 0);
            Place(BuildCard, 1, 0);
            Place(StateCard, 2, 0);
            Place(EventsCard, 3, 0);
        }
        else
        {
            Root.ColumnDefinitions = new ColumnDefinitions("*,*");
            Root.RowDefinitions = new RowDefinitions("Auto,*");
            Place(ConnectionCard, 0, 0);
            Place(BuildCard, 0, 1);
            Place(StateCard, 1, 0);
            Place(EventsCard, 1, 1);
        }
    }

    private static void Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
    }
}
