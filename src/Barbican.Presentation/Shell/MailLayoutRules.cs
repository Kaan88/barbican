// Mirrors: Bulwark 1.21.2 stores/settings-store.ts:448-466 (MailLayout, effectiveMailLayout: Mountain View and Simple mode are the focused layout), hooks/use-effective-mail-layout.ts:14-19
// (drawnMailLayout: a split layout too narrow for two panes opens a message in the list's place), components/mail/mail-app.tsx:5794-5844 (which pane shows when).

namespace Barbican.Presentation.Shell;

/// <summary>The pure decisions behind the mail layouts (SH-12): the layout in force and the panes it shows.</summary>
public static class MailLayoutRules
{
    /// <summary>The settings value of a layout.</summary>
    public static string Key(MailLayoutMode mode) => mode switch
    {
        MailLayoutMode.Focus => "focus",
        MailLayoutMode.Horizontal => "horizontal",
        _ => "split",
    };

    /// <summary>The layout of a saved <c>mailLayout</c> value; anything unknown is the default, split.</summary>
    public static MailLayoutMode Parse(string? value) => value switch
    {
        "focus" => MailLayoutMode.Focus,
        "horizontal" => MailLayoutMode.Horizontal,
        _ => MailLayoutMode.Split,
    };

    /// <summary>The layout in force (settings-store.ts:463-466): focus under Mountain View or Simple mode, the saved one otherwise. The saved value is never rewritten, so switching back restores it.</summary>
    public static MailLayoutMode Effective(MailLayoutMode saved, bool mountainView, bool simpleMode) => mountainView || simpleMode ? MailLayoutMode.Focus : saved;

    /// <summary>The panes to show. <paramref name="viewerHasContent"/> is a message, a conversation or a composer in the reading pane; <paramref name="tooNarrow"/> a window too narrow for the split layout's two panes.</summary>
    public static MailPanePlan Plan(MailLayoutMode layout, bool viewerHasContent, bool tooNarrow = false)
    {
        var drawn = layout == MailLayoutMode.Split && tooNarrow ? MailLayoutMode.Focus : layout;
        return drawn switch
        {
            // the reading pane stays beside the list even when empty: it holds the first-run tips
            MailLayoutMode.Split => new MailPanePlan(true, true, false, false, true),
            MailLayoutMode.Focus => viewerHasContent
                ? new MailPanePlan(false, true, false, false, false)
                : new MailPanePlan(true, false, false, true, false),
            _ => viewerHasContent
                ? new MailPanePlan(true, true, true, true, true)
                : new MailPanePlan(true, false, true, true, false),
        };
    }

    /// <summary>The stacked layout's list height: the saved one (or the fallback) kept short enough that the reading pane keeps its minimum height (mail-app.tsx:6543-6544 listPaneCssSize).</summary>
    public static double StackedListHeight(double saved, double availableHeight, double minList, double minReader, double fallback)
    {
        var wanted = saved > 0 ? saved : fallback;
        var most = availableHeight > 0 ? Math.Max(minList, availableHeight - minReader) : double.MaxValue;
        return Math.Clamp(wanted, minList, most);
    }
}
