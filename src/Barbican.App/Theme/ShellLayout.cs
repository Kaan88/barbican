// Bulwark 1.21.2: hooks/use-media-query.ts:12-33 (PHONE_BELOW = 600, FULL_DESKTOP = 1024, classifyWidth), components/layout/edge-drawer.tsx:63-66 (w-72, max-w-[85%] drawer),
// components/layout/navigation-rail.tsx:390-445 (bottom tab bar, min-h-[44px] items).
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

    /// <summary>Bulwark's phone layout begins below this window width (hooks/use-media-query.ts:12 PHONE_BELOW): one pane at a time, a bottom tab bar, the folders in a drawer.</summary>
    public const double PhoneBelow = 600;

    /// <summary>The folder drawer's width (edge-drawer.tsx:63 <c>w-72</c>) and its share of a narrow window (<c>max-w-[85%]</c>).</summary>
    public const double DrawerWidth = 288;

    public const double DrawerMaxShare = 0.85;

    /// <summary>The bottom tab bar item height (navigation-rail.tsx:420 <c>min-h-[44px]</c>).</summary>
    public const double TabBarItemHeight = 44;

    /// <summary>The Bulwark phone layout (a window narrower than <see cref="PhoneBelow"/>).</summary>
    public static bool IsPhone(double windowWidth) => windowWidth < PhoneBelow;

    /// <summary>The folder drawer's width in a window of this width.</summary>
    public static double DrawerWidthFor(double windowWidth) => Math.Min(DrawerWidth, Math.Max(0, windowWidth) * DrawerMaxShare);

    /// <summary>The reader width (reader-w) from which the summary tiles show (Bento).</summary>
    public const double TilesMinReader = 330;

    /// <summary>Computes the layout of the mail screen.</summary>
    public static ShellLayout Compute(double windowWidth, bool nordic, bool sidebarShown = true)
        => Compute(windowWidth, nordic ? ShellStyle.Nordic : ShellStyle.Bento, sidebarShown);

    public static ShellLayout Compute(double windowWidth, ShellStyle style, bool sidebarShown = true)
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

        return new ShellLayout(windowWidth, m.Nordic, sidebarShown, !m.Bulwark && !m.Nordic && reader >= TilesMinReader, list, reader, context);
    }
}
