using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace S4;

/// <summary>
/// Layer A/B alternative: a real UI Automation client (FlaUI.UIA3, patterns only) talking to our own window.
/// Used instead of in-process AutomationPeers because XAML peers made by CreatePeerForElement lack the patterns
/// (ListViewItem container peers expose none; TextBox exposes no Value pattern that way).
/// Nothing here moves the pointer or takes the keyboard.
/// </summary>
internal static class Uia
{
    static UIA3Automation automation;
    static readonly object Gate = new();

    public static string Run(string line, nint hwnd, MainWindow win)
    {
        lock (Gate)
        {
            automation ??= new UIA3Automation();
            var a = line.Split(' ', 3);
            var root = automation.FromHandle(hwnd);
            switch (a[0])
            {
                case "uia-click":
                {
                    var q = line.Split(' ');
                    var s = Native.Scale(hwnd);
                    var (cx, cy, _, _) = Native.ClientInFrame(hwnd);
                    Native.GetClientRect(hwnd, out _);
                    var org = new Native.POINT();
                    Native.ClientToScreen(hwnd, ref org);
                    var px = org.X + (int)(double.Parse(q[1], CultureInfo.InvariantCulture) * s);
                    var py = org.Y + (int)(double.Parse(q[2], CultureInfo.InvariantCulture) * s);
                    var best = root.FindAllDescendants()
                        .Where(e => { try { var r = e.BoundingRectangle; return r.Contains(px, py) && Acts(e); } catch { return false; } })
                        .OrderBy(e => e.BoundingRectangle.Width * e.BoundingRectangle.Height)
                        .FirstOrDefault();
                    if (best == null) return "nothing clickable";
                    return Do(best, line[4..]);
                }
                case "uia-invoke": case "uia-select": case "uia-text": case "uia-toggle":
                {
                    var el = root.FindFirstDescendant(cf => cf.ByAutomationId(a[1]));
                    if (el == null) return "no element " + a[1];
                    if (a[0] == "uia-text") { if (!el.Patterns.Value.IsSupported) return "no value pattern"; el.Patterns.Value.Pattern.SetValue(a.Length > 2 ? a[2] : ""); return "ok"; }
                    return Do(el, line[4..]);
                }
                case "uia-tree":
                    return string.Join(" | ", root.FindAllDescendants().Take(400).Select(e => $"{e.Properties.ControlType.ValueOrDefault}:{e.Properties.AutomationId.ValueOrDefault}:{e.Properties.Name.ValueOrDefault}:{(e.Patterns.Invoke.IsSupported ? "I" : "")}{(e.Patterns.SelectionItem.IsSupported ? "S" : "")}{(e.Patterns.Value.IsSupported ? "V" : "")}{(e.Patterns.Toggle.IsSupported ? "T" : "")}")
                        .Where(x => !x.StartsWith("Pane::", StringComparison.Ordinal)).Take(60));
                default: return "unknown command " + a[0];
            }
        }
    }

    static bool Acts(AutomationElement e) =>
        e.Patterns.Invoke.IsSupported || e.Patterns.Toggle.IsSupported || e.Patterns.SelectionItem.IsSupported || e.Patterns.ExpandCollapse.IsSupported;

    static string Do(AutomationElement e, string cmd)
    {
        Probe.Event(cmd);   // timestamp at the pattern call, not before the element search (which takes 30-100 ms)
        if (e.Patterns.Invoke.IsSupported) e.Patterns.Invoke.Pattern.Invoke();
        else if (e.Patterns.Toggle.IsSupported) e.Patterns.Toggle.Pattern.Toggle();
        else if (e.Patterns.SelectionItem.IsSupported) e.Patterns.SelectionItem.Pattern.Select();
        else if (e.Patterns.ExpandCollapse.IsSupported) e.Patterns.ExpandCollapse.Pattern.Expand();
        else return "pattern not supported";
        return "ok " + (e.Properties.AutomationId.ValueOrDefault is { Length: > 0 } id ? id : e.Properties.Name.ValueOrDefault);
    }
}
