// Mirrors: spec/layout/shell.md "Breakpoints and responsive rules" (app.slint:1722-1737); the explicit minimum window size is the open point of that file.

namespace Barbican.App.Theme;

/// <summary>Which regions show at a window width and how wide the columns are. Pure arithmetic, so the breakpoints are tested without a window.</summary>
public readonly record struct ShellLayout(double WindowWidth, bool Nordic, bool SidebarShown, bool SummaryTilesShown, double ListWidth, double ReaderWidth)
{
    /// <summary>The smallest window the C# app allows (spec/layout/shell.md suggests 960 x 640).</summary>
    public const double MinWidth = 960;

    public const double MinHeight = 640;

    /// <summary>The reader width (reader-w) from which the summary tiles show (Bento).</summary>
    public const double TilesMinReader = 330;

    /// <summary>Computes the layout of the mail screen.</summary>
    public static ShellLayout Compute(double windowWidth, bool nordic, bool sidebarShown = true)
    {
        var m = StyleMetrics.For(nordic);
        var list = m.ListWidth(windowWidth);
        var fixedWidth = m.Pad + m.RailWidth + m.Gap + (sidebarShown ? m.SidebarWidth + m.Gap : 0) + list + m.Gap + m.Pad;
        var reader = Math.Max(0, windowWidth - fixedWidth);
        return new ShellLayout(windowWidth, nordic, sidebarShown, !nordic && reader >= TilesMinReader, list, reader);
    }
}
