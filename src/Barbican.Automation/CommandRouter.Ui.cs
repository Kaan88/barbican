using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Automation.Capture;
using Barbican.Automation.Protocol;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
#if WINDOWS
using Barbican.Automation.Windows;
#endif

namespace Barbican.Automation;

/// <summary>The built-in commands that run on the UI thread.</summary>
internal sealed partial class CommandRouter
{
    private const string NotWeb = "\u0000not-web";

    private async Task<string> RunOnUiAsync(CommandLine line, string raw)
    {
        if (Inputs.Contains(line.Name))
        {
            Event(raw);
        }

        var root = _context.Root;
        switch (line.Name)
        {
            case "size":
            {
                var s = _context.Window.AppWindow.Size;
                return string.Create(CultureInfo.InvariantCulture, $"{s.Width} {s.Height} scale {Scale():0.##}");
            }

            case "resize":
            {
                var s = Scale();
                _context.Window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32 { Width = (int)(Math.Max(400, line.Number(0)) * s), Height = (int)(Math.Max(300, line.Number(1)) * s) });
                return "ok";
            }

            case "title":
                return _context.Window.Title;
            case "peer-click":
                return await PeerClickAsync(line.Number(0), line.Number(1));
#if !WINDOWS
            case "click":
            case "dclick":
            {
                var web = await WebClickAsync(line, string.Equals(line.Name, "dclick", StringComparison.Ordinal) ? 2 : 1);
                return string.Equals(web, NotWeb, StringComparison.Ordinal) ? await PeerClickAsync(line.Number(0), line.Number(1)) : web;
            }

            case "type":
                return TypeIntoFocused(line.Argument);
#endif
            case "rclick":
                return RightClick(line.Number(0), line.Number(1));
            case "move":
                return "ok";   // hover cannot be injected in-process (layer C): accepted as a no-op so scripts run
            case "drag":
                return "error drag needs real input (layer C, throwaway VM only)";
            case "scroll":
                return Scroll(line.Number(0), line.Number(1), line.Number(2));
            case "key":
                return "error no key router yet (the mailcore and shell-ui streams register 'key')";
            case "invoke":
                return WithPeer(line.Argument, p =>
                {
                    if (p.GetPattern(PatternInterface.Invoke) is not IInvokeProvider inv)
                    {
                        return false;
                    }

                    inv.Invoke();
                    return true;
                });
            case "select":
                return WithPeer(line.Argument, p =>
                {
                    if (p.GetPattern(PatternInterface.SelectionItem) is not ISelectionItemProvider s)
                    {
                        return false;
                    }

                    s.Select();
                    return true;
                });
            case "text":
            {
                var id = line.Token(0);
                var value = line.Argument.Length > id.Length ? line.Argument[(id.Length + 1)..] : string.Empty;
                // The WinUI peer does not always hand out the Value pattern to managed code (the M2 stage F run answered "pattern not supported" for every
                // TextBox); an in-process TextBox takes the text directly, which raises TextChanged exactly as typing does.
                if (ElementSearch.ById(_context.Root, id.Trim()) is Microsoft.UI.Xaml.Controls.TextBox box)
                {
                    box.Text = value;
                    return "ok";
                }

                return WithPeer(id, p =>
                {
                    if (p.GetPattern(PatternInterface.Value) is not IValueProvider v)
                    {
                        return false;
                    }

                    v.SetValue(value);
                    return true;
                });
            }

            case "tree":
                return Tree();
            case "vstate":
                return VisualState(root, line.Token(0), line.Token(1));
            case "bounds":
                return Bounds(root, line.Token(0));
            case "patterns":
                return Patterns(line.Number(0), line.Number(1));
            case "shot":
                return await XamlShotAsync(line.Argument);
            case "wshot":
                return await FrameProbe.WindowShotAsync(_context, line.Argument);
            case "burst":
                return _burst.Start(line, () => ElementSearch.First<WebView2>(root));
            case "burst-status":
                return _burst.Status();
            case "probe-start":
                return _probe.Start(line.Token(0));
            case "probe-stop":
                return _probe.Stop();
            case "every":
                return Every((int)line.Number(0), (int)line.Number(1), string.Join(' ', line.Tokens.Skip(2)));
            case "motion":
                return "ok";   // flag only until the motion settings exist (mailcore registers its own 'motion')
            case "theme":
            case "ui":
                return Theme(line);
            case "wv-eval":
                return await WebEvalAsync(line.Argument);
            case "focus-id":
                return FocusedId();
            case "focus-guard":
#if WINDOWS
                return Native.FocusReport(_context.Hwnd);
#else
                return "n/a on Linux (private Xvfb)";
#endif
            case "popup-guard":
                return _guard.Describe();
            case "popup-probe":
                return await PopupProbeAsync(line);
            case "close":
            {
                // After the reply is written: closing tears down the server.
                if (_closeTimer is not null) return "ok";
                var t = _context.Dispatcher.CreateTimer();
                _closeTimer = t; // Keep the one-shot timer alive until its tick.
                t.Interval = TimeSpan.FromMilliseconds(200);
                t.IsRepeating = false;
                t.Tick += (_, _) =>
                {
                    _closeTimer = null;
                    _ = CloseAfterReplyAsync();
                };
                t.Start();
                return "ok";
            }
            default:
                return "unknown command " + line.Name;
        }
    }

    private double Scale()
    {
#if WINDOWS
        return Native.Scale(_context.Hwnd);
#else
        return _context.Root?.XamlRoot?.RasterizationScale ?? 1.0;
#endif
    }

    private string FocusedId() =>
        _context.Root?.XamlRoot is { } xr && FocusManager.GetFocusedElement(xr) is FrameworkElement fe ? AutomationProperties.GetAutomationId(fe) : string.Empty;

    private string FocusById(string id) =>
        ElementSearch.ById(_context.Root, id) is Control c && c.Focus(FocusState.Programmatic) ? "ok" : "focus failed " + id;

#if !WINDOWS
    private string TypeIntoFocused(string text)
    {
        if (_context.Root?.XamlRoot is not { } xr || FocusManager.GetFocusedElement(xr) is not FrameworkElement fe)
        {
            return "nothing focused";
        }

        if (ElementSearch.Peer(fe)?.GetPattern(PatternInterface.Value) is not IValueProvider v)
        {
            return "focused element has no value pattern";
        }

        v.SetValue(v.Value + text);
        return "ok";
    }
#endif

    private async Task<string> WebClickAsync(CommandLine line, int count)
    {
        var root = _context.Root;
        var web = ElementSearch.First<WebView2>(root);
        if (root is null || web?.CoreWebView2 is null)
        {
            return NotWeb;
        }

        var x = line.Number(0);
        var y = line.Number(1);
        var o = web.TransformToVisual(root).TransformPoint(new global::Windows.Foundation.Point(0, 0));
        if (x < o.X || y < o.Y || x >= o.X + web.ActualWidth || y >= o.Y + web.ActualHeight)
        {
            return NotWeb;
        }

        var js = string.Create(CultureInfo.InvariantCulture, $"(()=>{{const e=document.elementFromPoint({x - o.X:0.##},{y - o.Y:0.##});if(!e)return 'none';for(let i=0;i<{count};i++)e.click();return e.id||e.tagName}})()");
        return "ok web " + (await web.ExecuteScriptAsync(js)).Trim('"');
    }

    private Task<string> PeerClickAsync(double x, double y)
    {
        if (_context.Root is not { } root)
        {
            return Task.FromResult("no window content");
        }

        var (el, peer) = ElementSearch.Target(root, x, y, PatternInterface.Invoke, PatternInterface.Toggle, PatternInterface.SelectionItem, PatternInterface.ExpandCollapse);
        if (peer is null || el is null)
        {
            foreach (var hit in ElementSearch.At(root, x, y))
            {
                for (DependencyObject? d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
                {
                    if (d is TextBox or PasswordBox && d is Control c)
                    {
                        _ = c.Focus(FocusState.Programmatic);
                        return Task.FromResult("ok focus " + c.Name);
                    }
                }
            }

            return Task.FromResult("nothing clickable");
        }

        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider inv)
        {
            inv.Invoke();
        }
        else if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider t)
        {
            t.Toggle();
        }
        else if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider s)
        {
            s.Select();
        }
        else if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider ec)
        {
            if (ec.ExpandCollapseState == ExpandCollapseState.Collapsed)
            {
                ec.Expand();
            }
            else
            {
                ec.Collapse();
            }
        }

        return Task.FromResult("ok " + (AutomationProperties.GetAutomationId(el) is { Length: > 0 } id ? id : el.GetType().Name));
    }

    private string RightClick(double x, double y)
    {
        if (_context.Root is not { } root)
        {
            return "no window content";
        }

        foreach (var hit in ElementSearch.At(root, x, y))
        {
            for (DependencyObject? d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is UIElement ue && ue.ContextFlyout is { } fl)
                {
                    fl.ShowAt(ue, new FlyoutShowOptions { Position = new global::Windows.Foundation.Point(8, 8) });
                    return "ok";
                }
            }
        }

        return "no context menu";
    }

    private string Scroll(double x, double y, double dy)
    {
        if (_context.Root is not { } root)
        {
            return "no window content";
        }

        foreach (var hit in ElementSearch.At(root, x, y))
        {
            for (DependencyObject? d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
            {
                var sv = d as ScrollViewer ?? ElementSearch.First<ScrollViewer>(d);
                if (sv is not null)
                {
                    _ = sv.ChangeView(null, sv.VerticalOffset + dy, null, true);
                    return "ok";
                }
            }
        }

        return "no scroll viewer";
    }

    private string WithPeer(string id, Func<AutomationPeer, bool> act)
    {
        var el = ElementSearch.ById(_context.Root, id.Trim());
        if (el is null)
        {
            return "no element " + id;
        }

        return ElementSearch.Peer(el) is { } p && act(p) ? "ok" : "pattern not supported";
    }

    private string Tree()
    {
        var items = new JsonArray();
        void Walk(DependencyObject d)
        {
            if (d is FrameworkElement fe && AutomationProperties.GetAutomationId(fe) is { Length: > 0 } id)
            {
                var pr = ElementSearch.Peer(fe);
                items.Add(new JsonObject { ["id"] = id, ["name"] = pr?.GetName(), ["type"] = pr?.GetAutomationControlType().ToString() });
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                Walk(VisualTreeHelper.GetChild(d, i));
            }
        }

        if (_context.Root is { } root)
        {
            Walk(root);
        }

        return items.ToJsonString();
    }

    private string Patterns(double x, double y)
    {
        if (_context.Root is not { } root)
        {
            return "no window content";
        }

        var sb = new StringBuilder();
        foreach (var hit in ElementSearch.At(root, x, y).Take(1))
        {
            for (DependencyObject? d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is FrameworkElement fe)
                {
                    var pr = ElementSearch.Peer(fe);
                    var got = pr is null ? [] : Enum.GetValues<PatternInterface>().Where(pi => pr.GetPattern(pi) is not null);
                    sb.Append(CultureInfo.InvariantCulture, $"{fe.GetType().Name}[{pr?.GetType().Name}]:{string.Join(",", got)} | ");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>vstate ID STATE</c>: puts the control with that AutomationId into a visual state (PointerOver, Normal, Pressed, ...) without any pointer.
    /// The stand-in for hover in a hidden run, where no real pointer may move (docs/milestones/M1-look-gap.md, tools/lookdiff.py).
    /// </summary>
    private static string VisualState(FrameworkElement? root, string id, string state)
    {
        if (ElementSearch.ById(root, id) is not Control control)
        {
            return "error no control " + id;
        }

        return VisualStateManager.GoToState(control, state, false) ? "ok" : "error no state " + state;
    }

    /// <summary><c>bounds ID</c>: <c>X Y W H</c> of the element with that AutomationId, in logical px relative to the window content.</summary>
    private static string Bounds(FrameworkElement? root, string id)
    {
        if (root is null || ElementSearch.ById(root, id) is not { } element || element.ActualWidth <= 0)
        {
            return "error no element " + id;
        }

        var p = element.TransformToVisual(root).TransformPoint(new global::Windows.Foundation.Point(0, 0));
        return string.Create(CultureInfo.InvariantCulture, $"{p.X:0.#} {p.Y:0.#} {element.ActualWidth:0.#} {element.ActualHeight:0.#}");
    }

    /// <summary>XAML only (RenderTargetBitmap): no WebView2 content, no windowed popups.</summary>
    private async Task<string> XamlShotAsync(string path)
    {
        if (_context.Root is not { } root)
        {
            return "no window content";
        }

        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var buf = await rtb.GetPixelsAsync();
        var bytes = new byte[buf.Length];
        using (var reader = global::Windows.Storage.Streams.DataReader.FromBuffer(buf))
        {
            reader.ReadBytes(bytes);
        }

        var shot = new Shot(rtb.PixelWidth, rtb.PixelHeight, rtb.PixelWidth * 4, bytes);
        await Task.Run(() => PngWriter.Save(shot, path));
        return string.Create(CultureInfo.InvariantCulture, $"saved {rtb.PixelWidth}x{rtb.PixelHeight}");
    }

    private string Theme(CommandLine line)
    {
        if (_context.Root is not { } root)
        {
            return "no window content";
        }

        var light = line.Tokens.Contains("light", StringComparer.Ordinal);
        var dark = line.Tokens.Contains("dark", StringComparer.Ordinal);
        if (light || dark)
        {
            root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        }

        return "ok";
    }

    private async Task<string> WebEvalAsync(string js)
    {
        var web = ElementSearch.First<WebView2>(_context.Root);
        if (web?.CoreWebView2 is null)
        {
            return "no webview";
        }

        return CommandLine.OneLine(await web.ExecuteScriptAsync(js));
    }

    /// <summary><c>every MS N CMD</c>: CMD N times, one per MS, from a UI-thread timer (A+Bi tokens expanded).</summary>
    private string Every(int ms, int n, string command)
    {
        var timer = _context.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        var i = 0;
        timer.Tick += (t, _) =>
        {
            if (i >= n)
            {
                t.Stop();
                return;
            }

            var c = EveryExpansion.Expand(command, i);
            i++;
            _ = DispatchAsync(c, CancellationToken.None);
        };
        timer.Start();
        return "ok";
    }
    private async Task CloseAfterReplyAsync()
    {
        try
        {
            if (_context.Close is { } close) await close().ConfigureAwait(false);
            else _context.Window.Close();
        }
#pragma warning disable CA1031 // A deferred command failure must be observable without escaping a timer callback.
        catch (Exception error)
#pragma warning restore CA1031
        {
            await Console.Error.WriteLineAsync("[automation] close failed: " + error.GetType().Name).ConfigureAwait(false);
        }
    }

}
