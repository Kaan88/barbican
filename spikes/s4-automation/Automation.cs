using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace S4;

/// <summary>
/// In-process automation server: the line protocol of the Rust app's automation.rs (one command per line,
/// one reply line), so tools/auto.py, motion.py, flicker.py work unchanged. No real input is ever produced:
/// clicks go through UIA automation peers, keys through KeyRouter, capture through WGC.
/// </summary>
internal static class Automation
{
    public static bool Enabled => Environment.GetEnvironmentVariable("BARBICAN_AUTOMATION") == "1";
    static MainWindow win;
    static nint hwnd;
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public static void Start(MainWindow w)
    {
        if (!Enabled) return;
        win = w; hwnd = Native.Hwnd(w);
        var port = int.TryParse(Environment.GetEnvironmentVariable("BARBICAN_AUTOMATION_PORT"), out var p) ? p : 8792;
        var l = new TcpListener(IPAddress.Loopback, port);   // loopback only
        l.Start();
        new Thread(() =>
        {
            while (true)
            {
                var c = l.AcceptTcpClient();
                using var r = new StreamReader(c.GetStream(), Encoding.UTF8);
                using var o = new StreamWriter(c.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    string reply;
                    try
                    {
                        // UIA-client commands run here, on the server thread: a UIA client call into our own UI thread
                        // from the UI thread would deadlock; from this thread it is an ordinary cross-thread provider call.
                        // click/dclick resolve through UIA too (in-proc peers lack patterns on item containers); peer-click keeps the XAML path.
                        var plain = false;
                        if (line.StartsWith("click ", StringComparison.Ordinal)) { line = "uia-" + line; plain = true; }
                        else if (line.StartsWith("peer-click ", StringComparison.Ordinal)) line = line[5..];
                        var t = line.StartsWith("uia-", StringComparison.Ordinal) ? Task.Run(() => Uia.Run(line, hwnd, win)) : OnUi(() => Run(line));
                        reply = t.Wait(TimeSpan.FromSeconds(20)) ? t.Result : "timeout";
                        if (plain && reply.StartsWith("ok ", StringComparison.Ordinal)) reply = "ok";
                    }
                    catch (Exception e) { reply = "error " + (e.InnerException ?? e).Message; }
                    o.WriteLine(reply.Replace('\n', ' ').Replace('\r', ' '));
                }
            }
        }) { IsBackground = true, Name = "automation" }.Start();
    }

    static Task<string> OnUi(Func<Task<string>> f)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!win.DispatcherQueue.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await f()); } catch (Exception e) { tcs.SetResult("error " + e.Message); }
        })) tcs.SetResult("error no dispatcher");
        return tcs.Task;
    }

    static readonly HashSet<string> Inputs = new() { "click", "rclick", "dclick", "move", "key", "scroll", "drag", "type", "invoke", "select", "text" };
    static FrameworkElement Root => (FrameworkElement)win.Content;

    static async Task<string> Run(string line)
    {
        var a = line.Split(' ', 2);
        var cmd = a[0];
        var arg = a.Length > 1 ? a[1] : "";
        var args = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Inputs.Contains(cmd)) Probe.Event(line);
        double P(int i) => args.Length > i && double.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        switch (cmd)
        {
            case "size":
            {
                var s = win.AppWindow.Size; return $"{s.Width} {s.Height} scale {Native.Scale(hwnd).ToString("0.##", CultureInfo.InvariantCulture)}";
            }
            case "resize": { var s = Native.Scale(hwnd); win.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(Math.Max(400, P(0)) * s), (int)(Math.Max(300, P(1)) * s))); return "ok"; }
            case "title": return win.Title;
            case "click": return await Click(P(0), P(1), 1);
            case "dclick": return await Click(P(0), P(1), 2);
            case "rclick": return await RClick(P(0), P(1));
            case "move": return "ok";   // hover cannot be injected in-process (Layer C); accepted as a no-op so scripts run
            case "scroll": return Scroll(P(0), P(1), P(2));
            case "type": return Type(arg);
            case "key": return KeyRouter.Handle(arg, Root) ? "ok" : "unknown key " + arg;
            case "invoke": return Pattern(arg, p => { (p.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke(); return p.GetPattern(PatternInterface.Invoke) != null; });
            case "select": return Pattern(arg, p => { (p.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider)?.Select(); return p.GetPattern(PatternInterface.SelectionItem) != null; });
            case "text":
            {
                var sp = arg.Split(' ', 2);
                return Pattern(sp[0], p => { var v = p.GetPattern(PatternInterface.Value) as IValueProvider; v?.SetValue(sp.Length > 1 ? sp[1] : ""); return v != null; });
            }
            case "tree": return Tree();
            case "patterns": return Patterns(P(0), P(1));
            case "status": return ((TextBlock)Root.FindName("Status")).Text;
            case "shot": return await Shot(arg);
            case "wshot": return await WShot(arg, false);
            case "wshot-pw": return await WShot(arg, true);
            case "burst": return Burst.Start(win, hwnd, args);
            case "burst-status": return Burst.Status();
            case "probe-start": return Probe.Start(win, hwnd, args.Length > 0 ? args[0] : "wgc");
            case "probe-stop": return Probe.Stop();
            case "every": return Every((int)P(0), (int)P(1), string.Join(' ', args.Skip(2)));
            case "motion": foreach (var o in args) win.Reduce = o is "reduce" or "calm" ? true : o is "normal" or "rapid" ? false : win.Reduce; return "ok";
            case "theme": Root.RequestedTheme = arg.Trim() == "light" ? ElementTheme.Light : ElementTheme.Dark; return "ok";
            case "anim": if (arg.Trim() == "sb") win.AnimateStoryboard(); else win.AnimateComposition(); return "ok";
            case "wv-eval": return await WvEval(arg);
            case "focus-guard": { Native.GetCursorPos(out var cp); return $"fg={Native.GetForegroundWindow()} cursor={cp.X},{cp.Y}"; }
            case "close": win.Close(); return "ok";
            default: return "unknown command " + cmd;
        }
    }

    // ---------- UIA-pattern input ----------

    static IEnumerable<UIElement> At(double x, double y) =>
        VisualTreeHelper.FindElementsInHostCoordinates(new Windows.Foundation.Point(x, y), (UIElement)win.Content, false);

    /// <summary>The nearest element at the point (or one of its ancestors) whose automation peer supports a click-like pattern.</summary>
    /// <summary>Peers are created lazily: FromElement alone returns null until UIA or someone asks for one.</summary>
    internal static AutomationPeer Peer(FrameworkElement fe) => FrameworkElementAutomationPeer.FromElement(fe) ?? FrameworkElementAutomationPeer.CreatePeerForElement(fe);

    static (FrameworkElement el, AutomationPeer peer) Target(double x, double y, params PatternInterface[] wanted)
    {
        foreach (var hit in At(x, y))
            for (DependencyObject d = hit; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is FrameworkElement fe && Peer(fe) is { } peer && wanted.Any(w => peer.GetPattern(w) != null))
                    return (fe, peer);
        return (null, null);
    }

    static async Task<string> Click(double x, double y, int count)
    {
        // WebView2 is a separate HWND: its content is clicked through the DOM, not through UIA.
        var web = (WebView2)Root.FindName("Web");
        if (web.CoreWebView2 != null && web.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0)) is var o
            && x >= o.X && y >= o.Y && x < o.X + web.ActualWidth && y < o.Y + web.ActualHeight)
        {
            var js = $"(()=>{{const e=document.elementFromPoint({x - o.X:0.##},{y - o.Y:0.##});if(!e)return 'none';for(let i=0;i<{count};i++)e.click();return e.id||e.tagName}})()";
            return "ok web " + (await web.ExecuteScriptAsync(js)).Trim('"');
        }
        var (el, peer) = Target(x, y, PatternInterface.Invoke, PatternInterface.Toggle, PatternInterface.SelectionItem, PatternInterface.ExpandCollapse);
        if (peer == null)
        {
            // Text boxes: focus is the click.
            foreach (var hit in At(x, y))
                for (DependencyObject d = hit; d != null; d = VisualTreeHelper.GetParent(d))
                    if (d is Control c && c is TextBox or PasswordBox or RichEditBox) { c.Focus(FocusState.Programmatic); return "ok focus " + c.Name; }
            return "nothing clickable";
        }
        for (var i = 0; i < count; i++)
        {
            if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider inv) inv.Invoke();
            else if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider t) t.Toggle();
            else if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider s) s.Select();
            else if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider ec) { if (ec.ExpandCollapseState == ExpandCollapseState.Collapsed) ec.Expand(); else ec.Collapse(); }
        }
        return "ok " + (AutomationProperties.GetAutomationId(el) is { Length: > 0 } id ? id : el.GetType().Name);
    }

    static Task<string> RClick(double x, double y)
    {
        foreach (var hit in At(x, y))
            for (DependencyObject d = hit; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is UIElement ue && ue.ContextFlyout is { } fl)
                {
                    fl.ShowAt(ue, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = new Windows.Foundation.Point(8, 8) });
                    return Task.FromResult("ok");
                }
        return Task.FromResult("no context menu");
    }

    static string Scroll(double x, double y, double dy)
    {
        foreach (var hit in At(x, y))
            for (DependencyObject d = hit; d != null; d = VisualTreeHelper.GetParent(d))
            {
                var sv = d as ScrollViewer ?? Descendant<ScrollViewer>(d);
                if (sv != null) { sv.ChangeView(null, sv.VerticalOffset + dy, null, true); return "ok"; }
            }
        return "no scroll viewer";
    }

    static T Descendant<T>(DependencyObject d) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) return t;
            if (Descendant<T>(c) is { } r) return r;
        }
        return null;
    }

    static string Type(string text)
    {
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is not FrameworkElement fe) return "nothing focused";
        if (Peer(fe)?.GetPattern(PatternInterface.Value) is not IValueProvider v) return "focused element has no value pattern";
        v.SetValue(v.Value + text);
        return "ok";
    }

    static string Pattern(string id, Func<AutomationPeer, bool> f)
    {
        var el = ById(Root, id.Trim());
        if (el == null) return "no element " + id;
        var peer = Peer(el);
        return f(peer) ? "ok" : "pattern not supported";
    }

    static FrameworkElement ById(DependencyObject d, string id)
    {
        if (d is FrameworkElement fe && AutomationProperties.GetAutomationId(fe) == id) return fe;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (ById(VisualTreeHelper.GetChild(d, i), id) is { } r) return r;
        return null;
    }

    /// <summary>Debug: for each ancestor of the element at the point, its peer class and the patterns it exposes.</summary>
    static string Patterns(double x, double y)
    {
        var sb = new StringBuilder();
        foreach (var hit in At(x, y).Take(1))
            for (DependencyObject d = hit; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is FrameworkElement fe)
                {
                    var pr = Peer(fe);
                    var got = pr == null ? Enumerable.Empty<PatternInterface>() : Enum.GetValues<PatternInterface>().Where(pi => pr.GetPattern(pi) != null);
                    sb.Append($"{fe.GetType().Name}[{pr?.GetType().Name}]:{string.Join(",", got)} | ");
                }
        return sb.ToString();
    }

    static string Tree()
    {
        var items = new List<object>();
        void Walk(DependencyObject d)
        {
            if (d is FrameworkElement fe && AutomationProperties.GetAutomationId(fe) is { Length: > 0 } id)
            {
                var pr = Peer(fe);
                items.Add(new { id, name = pr?.GetName(), type = pr?.GetAutomationControlType().ToString() });
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(Root);
        return JsonSerializer.Serialize(items);
    }

    // ---------- capture ----------

    /// <summary>XAML only (RenderTargetBitmap): does not include the WebView2 HWND or popups.</summary>
    static async Task<string> Shot(string path)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(Root);
        var buf = await rtb.GetPixelsAsync();
        var bytes = new byte[buf.Length]; Windows.Storage.Streams.DataReader.FromBuffer(buf).ReadBytes(bytes);
        await Capture.SavePngAsync(new Shot(rtb.PixelWidth, rtb.PixelHeight, bytes), path);
        return $"saved {rtb.PixelWidth}x{rtb.PixelHeight}";
    }

    static async Task<string> WShot(string path, bool printWindow)
    {
        var s = printWindow ? Capture.PrintWindow(hwnd) : await Capture.OnceAsync(hwnd);
        if (s == null) return "error capture failed";
        await Capture.SavePngAsync(s, path);
        return $"saved {s.W}x{s.H}";
    }

    static async Task<string> WvEval(string js)
    {
        var web = (WebView2)Root.FindName("Web");
        if (web.CoreWebView2 == null) return "no webview";
        return (await web.ExecuteScriptAsync(js)).Replace('\n', ' ');
    }

    /// <summary>`every MS N CMD`: CMD runs N times, one per MS, from a UI-thread timer. Like automation.rs, a token
    /// "A+Bi" becomes A + B*i for the i-th run (a pointer sweep, one row per step).</summary>
    static string Every(int ms, int n, string cmd)
    {
        var timer = win.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        var i = 0;
        timer.Tick += (t, _) =>
        {
            if (i >= n) { t.Stop(); return; }
            var c = string.Join(' ', cmd.Split(' ').Select(w => System.Text.RegularExpressions.Regex.Match(w, @"^(-?[\d.]+)\+([\d.]+)i$") is { Success: true } m
                ? (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * i).ToString(CultureInfo.InvariantCulture) : w));
            i++;
            if (c.StartsWith("click ", StringComparison.Ordinal)) _ = Task.Run(() => Uia.Run("uia-" + c, hwnd, win));   // UIA client calls must not run on the UI thread
            else { if (Inputs.Contains(c.Split(' ')[0])) Probe.Event(c); _ = Run(c); }
        };
        timer.Start();
        return "ok";
    }

    internal static double Now() => NowMs();
    internal static MainWindow Window => win;
}

/// <summary>The one place keys are interpreted (PORT-PLAN.md section 4.4): real KeyDown handlers and the automation channel both call it.</summary>
internal static class KeyRouter
{
    public static bool Handle(string chord, FrameworkElement root)
    {
        switch (chord)
        {
            case "Down": case "Up":
            {
                var l = (ListView)root.FindName("MailList");
                var i = Math.Clamp(l.SelectedIndex + (chord == "Down" ? 1 : -1), 0, l.Items.Count - 1);
                l.SelectedIndex = i; l.ScrollIntoView(l.SelectedItem); return true;
            }
            case "Tab": return FocusManager.TryMoveFocus(FocusNavigationDirection.Next);
            case "Enter":
                if (FocusManager.GetFocusedElement(root.XamlRoot) is FrameworkElement fe
                    && Automation.Peer(fe)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider inv) { inv.Invoke(); return true; }
                return false;
            case "Ctrl+N": Automation.Window.Dispatch_Compose(); return true;
            case "Escape":
                foreach (var p in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)) p.IsOpen = false;
                return true;
            default: return false;
        }
    }
}
