// Mirrors: spec/layout/shell.md "Breakpoints and responsive rules" (app.slint:1722-1737); the explicit minimum window size is the open point of that file.
// The sender panel beside the reader comes from the WinUI prototype (prototype/winui/MainWindow.xaml ContextTile); unlike the prototype, it hides
// when the reader would get narrower than StyleMetrics.ContextMinReader (docs/milestones/M1-look-gap.md).

using Barbican.Presentation.Shell;

namespace Barbican.App.Theme;

/// <summary>Which regions show at a window width and how wide the columns are. Pure arithmetic, so the breakpoints are tested without a window.</summary>
public readonly record struct ShellLayout(double WindowWidth, bool Nordic, bool SidebarShown, bool SummaryTilesShown, double ListWidth, double ReaderWidth, bool ContextShown = false)
{
    /// <summary>The smallest window the C# app allows (spec/layout/shell.md suggests 960 x 640).</summary>
    public const double MinWidth = 960;

    public const double MinHeight = 640;

    /// <summary>The reader width (reader-w) from which the summary tiles show (Bento).</summary>
    public const double TilesMinReader = 330;

    /// <summary>Computes the layout of the mail screen.</summary>
    public static ShellLayout Compute(double windowWidth, bool nordic, bool sidebarShown = true, bool showSummaryTiles = false)
        => Compute(windowWidth, nordic ? ShellStyle.Nordic : ShellStyle.Bento, sidebarShown, showSummaryTiles);

    public static ShellLayout Compute(double windowWidth, ShellStyle style, bool sidebarShown = true, bool showSummaryTiles = false)
    {
        var m = StyleMetrics.For(style);
        var list = m.ListWidth(windowWidth);
        var fixedWidth = m.PadLeft + m.RailWidth + m.Gap + (sidebarShown ? m.SidebarWidth + m.Gap : 0) + list + m.Gap + m.Pad;
        var reader = Math.Max(0, windowWidth - fixedWidth);
        var withContext = reader - m.ContextWidth - m.Gap;
        var context = !m.Bulwark && withContext >= m.ContextMinReader;
        if (context)
        {
            reader = withContext;
        }

        return new ShellLayout(windowWidth, m.Nordic, sidebarShown, showSummaryTiles && !m.Nordic && reader >= TilesMinReader, list, reader, context);
    }
}
