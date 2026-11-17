#if WINDOWS
using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Barbican.Automation.Windows;

/// <summary>
/// A UI Automation client (FlaUI.UIA3, patterns only) talking to the app's own window (docs/adr/0007
/// decision 3; port of spikes/s4-automation/Uia.cs). In-process XAML peers made by
/// CreatePeerForElement lack the patterns of item containers and text boxes, a UIA client sees the
/// real ones. Must run off the UI thread (a UIA call from the UI thread into its own providers
/// deadlocks). Nothing here moves the pointer or takes the keyboard; FlaUI.Core.Input is never used.
/// </summary>
internal sealed class Uia : IDisposable
{
    private readonly Lock _gate = new();
    private readonly nint _hwnd;
    private readonly Action<string> _event;
    private UIA3Automation? _automation;

    /// <param name="hwnd">The app window.</param>
    /// <param name="event">Records an input event at the moment of the pattern call (probe timestamps).</param>
    public Uia(nint hwnd, Action<string> @event)
    {
        _hwnd = hwnd;
        _event = @event;
    }

    /// <summary>Runs one uia-* command; a reply starting with <c>focus:</c> asks the caller to focus that AutomationId through XAML.</summary>
    public string Run(string line)
    {
        lock (_gate)
        {
            _automation ??= new UIA3Automation();
            var a = line.Split(' ', 3);
            var root = _automation.FromHandle(_hwnd);
            switch (a[0])
            {
                case "uia-click":
                case "uia-dclick":
                    return Click(root, line, string.Equals(a[0], "uia-dclick", StringComparison.Ordinal) ? 2 : 1);
                case "uia-append":
                {
                    var el = a.Length > 1 ? root.FindFirstDescendant(cf => cf.ByAutomationId(a[1])) : null;
                    if (el is null || !el.Patterns.Value.IsSupported)
                    {
                        return "no value pattern";
                    }

                    _event(line);
                    el.Patterns.Value.Pattern.SetValue((el.Patterns.Value.Pattern.Value.ValueOrDefault ?? string.Empty) + (a.Length > 2 ? a[2] : string.Empty));
                    return "ok";
                }

                case "uia-invoke":
                case "uia-select":
                case "uia-toggle":
                case "uia-text":
                {
                    var el = a.Length > 1 ? root.FindFirstDescendant(cf => cf.ByAutomationId(a[1])) : null;
                    if (el is null)
                    {
                        return "no element " + (a.Length > 1 ? a[1] : string.Empty);
                    }

                    if (string.Equals(a[0], "uia-text", StringComparison.Ordinal))
                    {
                        if (!el.Patterns.Value.IsSupported)
                        {
                            return "no value pattern";
                        }

                        _event(line);
                        el.Patterns.Value.Pattern.SetValue(a.Length > 2 ? a[2] : string.Empty);
                        return "ok";
                    }

                    return Act(el, line[4..]);
                }

                case "uia-find":
                    return string.Join(" | ", root.FindAllDescendants()
                        .Where(e => (e.Properties.Name.ValueOrDefault ?? string.Empty).Contains(a.Length > 1 ? a[1] : string.Empty, StringComparison.OrdinalIgnoreCase))
                        .Select(e => $"{e.Properties.ControlType.ValueOrDefault}:{e.Properties.AutomationId.ValueOrDefault}:{e.Properties.Name.ValueOrDefault}:{e.BoundingRectangle}:{(e.Patterns.Invoke.IsSupported ? "I" : string.Empty)}"));
                case "uia-tree":
                    return string.Join(" | ", root.FindAllDescendants().Take(4000)
                        .Select(e => $"{e.Properties.ControlType.ValueOrDefault}:{e.Properties.AutomationId.ValueOrDefault}:{e.Properties.Name.ValueOrDefault}:{(e.Patterns.Invoke.IsSupported ? "I" : string.Empty)}{(e.Patterns.SelectionItem.IsSupported ? "S" : string.Empty)}{(e.Patterns.Value.IsSupported ? "V" : string.Empty)}{(e.Patterns.Toggle.IsSupported ? "T" : string.Empty)}")
                        .Where(x => !x.StartsWith("Pane::", StringComparison.Ordinal)).Take(200));
                default:
                    return "unknown command " + a[0];
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _automation?.Dispose();
            _automation = null;
        }
    }

    private static bool Acts(AutomationElement e) =>
        e.Patterns.Invoke.IsSupported || e.Patterns.Toggle.IsSupported || e.Patterns.SelectionItem.IsSupported || e.Patterns.ExpandCollapse.IsSupported;

    private static bool Contains(AutomationElement e, int x, int y)
    {
        try
        {
            return e.BoundingRectangle.Contains(x, y);
        }
#pragma warning disable CA1031 // an element can vanish between the search and the read
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private string Click(AutomationElement root, string line, int count)
    {
        var q = line.Split(' ');
        if (q.Length < 3
            || !double.TryParse(q[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lx)
            || !double.TryParse(q[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ly))
        {
            return "error usage: click X Y";
        }

        var s = Native.Scale(_hwnd);
        var (ox, oy) = Native.ClientOrigin(_hwnd);
        var px = ox + (int)(lx * s);
        var py = oy + (int)(ly * s);
        var best = root.FindAllDescendants()
            .Where(e => Contains(e, px, py) && Acts(e))
            .OrderBy(e => e.BoundingRectangle.Width * e.BoundingRectangle.Height)
            .FirstOrDefault();
        if (best is null)
        {
            // A text box has no click pattern: the click is a focus, done through XAML (UIA SetFocus could activate the window).
            var edit = root.FindAllDescendants().FirstOrDefault(e => Contains(e, px, py)
                && e.Properties.ControlType.ValueOrDefault == ControlType.Edit
                && e.Properties.AutomationId.ValueOrDefault is { Length: > 0 });
            return edit is not null ? "focus:" + edit.Properties.AutomationId.Value : "nothing clickable";
        }

        var reply = string.Empty;
        for (var i = 0; i < count; i++)
        {
            reply = Act(best, line[4..]);
        }

        return reply;
    }

    private string Act(AutomationElement e, string command)
    {
        _event(command);   // the timestamp at the pattern call, not before the 30-100 ms element search
        if (e.Patterns.Invoke.IsSupported)
        {
            e.Patterns.Invoke.Pattern.Invoke();
        }
        else if (e.Patterns.Toggle.IsSupported)
        {
            e.Patterns.Toggle.Pattern.Toggle();
        }
        else if (e.Patterns.SelectionItem.IsSupported)
        {
            e.Patterns.SelectionItem.Pattern.Select();
        }
        else if (e.Patterns.ExpandCollapse.IsSupported)
        {
            var p = e.Patterns.ExpandCollapse.Pattern;
            if (p.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Collapsed)
            {
                p.Expand();
            }
            else
            {
                p.Collapse();
            }
        }
        else
        {
            return "pattern not supported";
        }

        return "ok " + (e.Properties.AutomationId.ValueOrDefault is { Length: > 0 } id ? id : e.Properties.Name.ValueOrDefault);
    }
}
#endif
