using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace Barbican.Spike.BodyShell;

/// <summary>
/// The host side of the body shell (port of the Rust mailview.rs show/ready/reveal/cancel/dim, minus
/// the capture-under-overlay machinery, which WinUI does not need). One WebView2 navigates once, to
/// the shell; every message after that is a frame inside it.
/// </summary>
internal sealed class ShellHost
{
    private readonly WebView2 _view;
    private readonly List<string> _queued = [];
    private bool _shellNavigated;
    private long _seq;

    public ShellHost(WebView2 view)
    {
        _view = view;
    }

    /// <summary>How commands reach the shell: ExecuteScriptAsync, or a web message (large pages).</summary>
    public enum Transport
    {
        Script,
        Message,
    }

    public bool ShellUp { get; private set; }

    public Action<long>? OnReady { get; set; }

    public Action<string>? OnMessage { get; set; }

    /// <summary>Main-frame navigations after the shell, and new windows: blocked and recorded (they would be routed to the browser or composer).</summary>
    public List<string> Blocked { get; } = [];

    /// <summary>Remote requests the resource filter refused (Windows only; defence in depth on top of the CSP).</summary>
    public int FilteredRemote { get; private set; }

    /// <summary>Whether the resource filter lets remote loads through (per message, like "Load images").</summary>
    public bool AllowRemote { get; set; }

    public bool ResourceFilterInstalled { get; private set; }

    public List<string> SetupNotes { get; } = [];

    public async Task InitializeAsync(string shellHtml, Color background)
    {
        _view.DefaultBackgroundColor = background;
        await _view.EnsureCoreWebView2Async();
        var core = _view.CoreWebView2;
        TrySet(() => core.Settings.AreDevToolsEnabled = false, "AreDevToolsEnabled");
        TrySet(() => core.Settings.AreDefaultContextMenusEnabled = false, "AreDefaultContextMenusEnabled");
        TrySet(() => core.Settings.IsStatusBarEnabled = false, "IsStatusBarEnabled");
        TrySet(() => core.Settings.IsZoomControlEnabled = false, "IsZoomControlEnabled");
        _view.WebMessageReceived += (_, e) => Received(e.TryGetWebMessageAsString());
        _view.NavigationStarting += (_, e) =>
        {
            if (!_shellNavigated)
            {
                _shellNavigated = true;
                return;
            }

            e.Cancel = true;
            Blocked.Add("navigate " + e.Uri);
        };
        TrySet(() => core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Blocked.Add("new-window " + e.Uri);
        }, "NewWindowRequested");
#if WINDOWS
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var uri = e.Request.Uri;
            if (!AllowRemote && (uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https:", StringComparison.OrdinalIgnoreCase)))
            {
                FilteredRemote++;
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
            }
        };
        ResourceFilterInstalled = true;
#endif
        _view.NavigateToString(shellHtml);
    }

    /// <summary>Loads a page hidden next to the one on screen; <see cref="OnReady"/> fires with its number once laid out.</summary>
    public long Show(string html, string background, bool script, Transport transport)
    {
        var seq = ++_seq;
        if (transport == Transport.Script)
        {
            Run($"window.__bb&&__bb.show({seq},{Json(html)},{Json(background)},{Json(MailDocument.Sandbox(script))})");
        }
        else
        {
            var msg = JsonSerializer.Serialize(new ShellCommand("show", seq, html, background, MailDocument.Sandbox(script)), ShellJson.Default.ShellCommand);
            Post(msg);
        }

        return seq;
    }

    public void Reveal(long seq) => Run($"window.__bb&&__bb.reveal({seq.ToString(CultureInfo.InvariantCulture)})");

    public void Cancel() => Run($"window.__bb&&__bb.cancel({(++_seq).ToString(CultureInfo.InvariantCulture)})");

    public void Dim(bool on) => Run($"window.__bb&&__bb.dim({(on ? "true" : "false")})");

    public Task<string> EvalAsync(string js) => _view.ExecuteScriptAsync(js).AsTask();

    private static string Json(string s) => JsonSerializer.Serialize(s, ShellJson.Default.String);

    private void TrySet(Action set, string what)
    {
        try
        {
            set();
        }
        catch (Exception e)
        {
            SetupNotes.Add($"{what}: {e.GetType().Name}: {e.Message}");
        }
    }

    private void Received(string? msg)
    {
        if (msg is null)
        {
            return;
        }

        OnMessage?.Invoke(msg);
        if (msg == "shell")
        {
            ShellUp = true;
            foreach (var js in _queued)
            {
                _ = _view.ExecuteScriptAsync(js);
            }

            _queued.Clear();
        }
        else if (msg.StartsWith("ready:", StringComparison.Ordinal) && long.TryParse(msg.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            OnReady?.Invoke(n);
        }
    }

    private void Run(string js)
    {
        if (!ShellUp)
        {
            _queued.Add(js);
            return;
        }

        _ = _view.ExecuteScriptAsync(js);
    }

    private void Post(string json)
    {
        if (!ShellUp)
        {
            throw new InvalidOperationException("the shell is not up yet");
        }

        _view.CoreWebView2.PostWebMessageAsJson(json);
    }
}
