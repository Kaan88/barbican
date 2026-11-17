namespace Barbican.App.Shell;

/// <summary>The shell page; it only composes regions (PORT-PLAN.md §3.2).</summary>
public sealed partial class ShellPage : Page
{
    public ShellPage()
    {
        InitializeComponent();
        var gap = Token("Shape.Gap");
        var tile = new CornerRadius(Token("Shape.RTile"));
        Root.Padding = new Thickness(gap);
        Root.ColumnSpacing = gap;
        RailColumn.Width = new GridLength(Token("Shape.RailW"));
        Rail.CornerRadius = tile;
        Main.CornerRadius = tile;
    }

    private static double Token(string key) => (double)Application.Current.Resources[key];
}
