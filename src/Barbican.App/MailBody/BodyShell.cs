// Mirrors: Rust mailview.rs (tag rust-final): SHELL_JS :51 (now Assets/mail/shell.js), shell_html :127 (Core ShellDocument), sandbox :140
// (Core MailSandbox), show :158, slow :178, cancel_pending :190, dim :199, ready :206, theme_changed :232, clear :240, on_ipc :326, build :369
// (navigation and new-window handlers), paint_host :100 (here DefaultBackgroundColor). PORT-PLAN.md §3.6; docs/adr/0005 (decisions 1, 4).
// Bulwark components/email/email-viewer.tsx (sandboxed srcdoc iframe per message), lib/email-sanitization.ts:109 (frame CSP). Owner body.
//
// One persistent WebView2 document (the shell) that never navigates after its first page. Each message is a sandboxed srcdoc frame with
// its own CSP, loaded hidden next to the visible one; the shell posts ready:N once it is laid out (and dark-converted), the loader applies
// the native header in OnReady, and reveal(N) goes out in the same UI-thread turn. Nothing blank, white or black shows in between: the
// WebView's own background, the shell page and each frame are painted in the colour of the page about to show.
// Windows: commands go as web messages (one ordered channel, no 2 MB limit); a WebResourceRequested filter refuses remote requests unless
// the message allows them (defence in depth on top of the CSP); frame navigations other than about:srcdoc are cancelled. Linux (Uno,
// WebKitGTK): commands go through ExecuteScriptAsync (no PostWebMessageAs*), the CSP is the only remote-content guard.

#if WINDOWS
using System.Buffers;
#else
using System.Globalization;
#endif
using System.Text.Json;
using Barbican.App.Theme;
using Barbican.Core.Mail.Body;
using Barbican.Presentation.Common;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail.Loading;
using Microsoft.Extensions.Logging;
#if WINDOWS
using Microsoft.Web.WebView2.Core;
#endif

namespace Barbican.App.MailBody;

/// <summary>The body view's host: the warm WebView2 shell, driven by the message loader.</summary>
public sealed partial class BodyShell : IBodyPresenter, IStartupTask, IDisposable
{
    /// <summary>A page not ready after this long is slow (a large mail): the loading bar shows until it is.</summary>
    public static readonly TimeSpan SlowAfter = TimeSpan.FromMilliseconds(600);

    /// <summary>The last resort for a view that never answers.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMilliseconds(8000);

    private readonly BodyAppearance _appearance;
    // resolved on the first forwarded key: the key router needs the list, the list the loader, the loader this view (a cycle at construction)
    private readonly Lazy<IKeyRouter> _keys;
    private long _sentAt;
    private readonly ILinkRouter _links;
    private readonly IDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ThemeController _theme;
    private readonly ILogger<BodyShell> _log;
    private readonly ShellMessageGate _gate = new();
    private readonly List<string> _queued = [];
    private WebView2? _view;
    private bool _starting;
    private bool _shellUp;
    private bool _shellNavigated;
    private long _docSeq;
    private (long Seq, BodyRequest Request, MailPage Page)? _pending;
    private (BodyRequest Request, MailPage Page)? _current;
    private ITimer? _readyTimer;
    private bool _slow;
    private bool _loaderLoading;
    private string? _allowRemoteFor;
    private string? _formattedFor;
    private bool _remoteNow;

    public BodyShell(BodyAppearance appearance, Lazy<IKeyRouter> keys, ILinkRouter links, IDispatcher dispatcher, TimeProvider time, ThemeController theme, ILogger<BodyShell> log)
    {
        _appearance = appearance;
        _keys = keys;
        _links = links;
        _dispatcher = dispatcher;
        _time = time;
        _theme = theme;
        _log = log;
        _theme.CssApplied += OnThemeApplied;
    }

    /// <summary>The page on screen changed (banners: external content blocked, shown as plain text) or the loading state did.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The WebView the body view hosts (created on first use, on the UI thread).</summary>
    public WebView2 View => _view ??= CreateView();

    /// <summary>The shell document is up.</summary>
    public bool IsShellUp => _shellUp;

    /// <summary>The number of the last document sent.</summary>
    public long DocumentSequence => _docSeq;

    /// <summary>The message on screen, or null.</summary>
    public string? ShownId => _current?.Request.EmailId;

    /// <summary>The loading bar is on (the loader's slow state or a slow page).</summary>
    public bool IsLoading => _loaderLoading || _slow;

    /// <summary>The message on screen has remote content that is blocked.</summary>
    public bool RemoteBlocked => _current is { } c && c.Page.HasRemoteContent && !_remoteNow;

    /// <summary>The message on screen is shown as plain text by the accessibility setting (it has HTML).</summary>
    public bool ShownAsPlainText => _current is { } c && c.Request.Content.Html is not null && _appearance.A11y.PlainView && !string.Equals(_formattedFor, c.Request.EmailId, StringComparison.Ordinal);

    /// <summary>Page messages dropped by the gate.</summary>
    public long DroppedMessages => _gate.Dropped;

    /// <summary>Links handed to the link router.</summary>
    public int RoutedLinks { get; private set; }

    /// <summary>From the last page sent to the shell to its ready signal (layout and the dark pass done), or null.</summary>
    public TimeSpan? LastSendToReady { get; private set; }

    /// <summary>Navigations and new windows refused.</summary>
    public int BlockedNavigations { get; private set; }

    /// <summary>Remote requests the Windows resource filter refused.</summary>
    public int FilteredRequests { get; private set; }

    /// <summary>Starts the shell warm, before the first message is opened.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = View;
        return EnsureStartedAsync();
    }

    public void Show(BodyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(_allowRemoteFor, request.EmailId, StringComparison.Ordinal))
        {
            _allowRemoteFor = null;
        }

        if (!string.Equals(_formattedFor, request.EmailId, StringComparison.Ordinal))
        {
            _formattedFor = null;
        }

        var allow = !request.BlockRemote && string.Equals(_allowRemoteFor, request.EmailId, StringComparison.Ordinal);
        var plain = _appearance.A11y.PlainView && _formattedFor is null;
        var page = MailPageBuilder.Build(request.Content.BodyMarkup(plain), _appearance.Options(allow));
        Send(request, page, allow);
    }

    public void CancelPending()
    {
        var seq = ++_docSeq;
        _pending = null;
        StopReadyTimer();
        SetSlow(false);
        Command("cancel", seq);
    }

    public void SetDimmed(bool dimmed) => Command("dim", on: dimmed);

    public void SetLoading(bool loading)
    {
        if (_loaderLoading != loading)
        {
            _loaderLoading = loading;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        ++_docSeq;
        _pending = null;
        _current = null;
        StopReadyTimer();
        SetSlow(false);
        _loaderLoading = false;
        var bg = _appearance.ReaderHex;
        Command("clear", bg: bg);
        PaintHost(bg);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Load external content" for the message on screen (per message; spec I-209).</summary>
    public void LoadExternalContent()
    {
        if (_current is { } c && !c.Request.BlockRemote)
        {
            _allowRemoteFor = c.Request.EmailId;
            Rerender();
        }
    }

    /// <summary>"Show formatted" for the message on screen shown as plain text (spec I-214).</summary>
    public void ShowFormatted()
    {
        if (_current is { } c)
        {
            _formattedFor = c.Request.EmailId;
            Rerender();
        }
    }

    /// <summary>Runs a script in the shell document (automation and probes).</summary>
    public async Task<string> EvalAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return _view is null ? "null" : await _view.ExecuteScriptAsync(script) ?? "null";
    }

    public void Dispose()
    {
        _theme.CssApplied -= OnThemeApplied;
        _readyTimer?.Dispose();
    }

    private WebView2 CreateView()
    {
        var view = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
#if WINDOWS
        // the WebView's own surface is never white: it is painted in the reader's colour before the shell's first frame
        view.DefaultBackgroundColor = _appearance.ReaderColor;
#endif
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(view, "reader.body.web");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(view, "Message body");
        view.WebMessageReceived += (_, e) =>
        {
            string? raw;
            try
            {
                raw = e.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                raw = null;
            }

            OnMessage(raw);
        };
        view.NavigationStarting += (_, e) =>
        {
            // the shell itself is the only page; anything after it is refused (links reach the app as link:URL)
            if (!_shellNavigated)
            {
                _shellNavigated = true;
                return;
            }

            e.Cancel = true;
            BlockedNavigations++;
        };
        view.Loaded += (_, _) => _ = EnsureStartedAsync();
        return view;
    }

    private async Task EnsureStartedAsync()
    {
        if (_starting || _view is null || _view.XamlRoot is null)
        {
            return;
        }

        _starting = true;
        try
        {
            var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "mail", "shell.js")).ConfigureAwait(true);
            await _view.EnsureCoreWebView2Async();
            Configure(_view);
            _view.NavigateToString(ShellDocument.Build(script, _appearance.ReaderHex));
        }
#pragma warning disable CA1031 // a body view that cannot start leaves the reader without a body, never the app down
        catch (Exception e)
#pragma warning restore CA1031
        {
            LogStartFailed(_log, e);
            _starting = false;
        }
    }

    private void Configure(WebView2 view)
    {
        var core = view.CoreWebView2;
        Try(() => core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            BlockedNavigations++;
        });
#if WINDOWS
        // Uno implements none of these settings (WebKitGTK: no dev tools unless enabled; shell.js cancels the context menu itself)
        core.Settings.AreDevToolsEnabled = DevTools;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.FrameNavigationStarting += (_, e) =>
        {
            // a message frame may only ever hold its srcdoc (a meta refresh or a script-free self navigation would load a remote page)
            if (!e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                BlockedNavigations++;
            }
        };
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var uri = e.Request.Uri;
            var remote = uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
            if (remote && !_remoteNow)
            {
                FilteredRequests++;
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
            }
        };
#endif
    }

#if DEBUG || AUTOMATION
    private const bool DevTools = true;
#else
    private const bool DevTools = false;
#endif

    private void Try(Action set)
    {
        try
        {
            set();
        }
#pragma warning disable CA1031 // Uno implements part of CoreWebView2 only; a missing setting is logged, the shell still works
        catch (Exception e)
#pragma warning restore CA1031
        {
            LogSettingFailed(_log, e);
        }
    }

    private void Send(BodyRequest request, MailPage page, bool allowRemote)
    {
        var seq = ++_docSeq;
        _pending = (seq, request, page);
        _sentAt = _time.GetTimestamp();
        StopReadyTimer();
        _readyTimer = _time.CreateTimer(_ => _dispatcher.Post(() => Slow(seq)), null, SlowAfter, Timeout.InfiniteTimeSpan);

        // the filter follows the newest page (the old one stays as loaded); a page that is not allowed remote content never gets it
        _remoteNow = allowRemote;
        Command("show", seq, html: page.Html, bg: page.Background, sandbox: MailSandbox.For(page.HasScript));
    }

    // Document seq is not ready after 600 ms: with the shell up it is still parsing (a large mail): the loading bar shows and the page is
    // waited for, with a last resort. Without a shell there is nothing to wait for.
    private void Slow(long seq)
    {
        if (_pending?.Seq != seq)
        {
            return;
        }

        if (!_shellUp)
        {
            Ready(seq);
            return;
        }

        SetSlow(true);
        StopReadyTimer();
        _readyTimer = _time.CreateTimer(_ => _dispatcher.Post(() => Ready(seq)), null, GiveUpAfter, Timeout.InfiniteTimeSpan);
    }

    // Document seq is laid out (or the wait gave up): the native side updates, then the page is revealed in the same turn.
    private void Ready(long seq)
    {
        if (_pending is not { } p || p.Seq != seq || seq != _docSeq)
        {
            return;
        }

        StopReadyTimer();
        _pending = null;
        LastSendToReady = _time.GetElapsedTime(_sentAt);
        SetSlow(false);
        if (!p.Request.OnReady())
        {
            // the native side refused it (the message it belongs to is no longer the open one)
            CancelPending();
            return;
        }

        _current = (p.Request, p.Page);
        PaintHost(p.Page.Background);
        Command("reveal", seq);
        StateChanged?.Invoke(this, EventArgs.Empty);
        p.Request.OnShown?.Invoke();
    }

    private void Rerender()
    {
        // a page on its way keeps its own callbacks; it is built with the new settings when sent again
        if (_pending is { } p)
        {
            Show(p.Request);
            return;
        }

        if (_current is { } c)
        {
            Show(c.Request with { OnReady = static () => true, OnShown = null });
        }
    }

    private void OnThemeApplied(object? sender, ThemeCssEventArgs e)
    {
        // the open message is built again in the new colours and swapped in once ready (no flash); an empty view takes the reader's colour
        if (_current is not null || _pending is not null)
        {
            Rerender();
            return;
        }

        var bg = _appearance.ReaderHex;
        Command("clear", bg: bg);
        PaintHost(bg);
    }

    private void PaintHost(string hex)
    {
#if WINDOWS
        if (_view is not null)
        {
            _view.DefaultBackgroundColor = BodyAppearance.Parse(hex);
        }
#else
        // Uno has no DefaultBackgroundColor: the shell page and each frame carry the colour
        _ = hex;
        _ = _view;
#endif
    }

    private void OnMessage(string? raw)
    {
        if (!_gate.TryAccept(raw, out var m))
        {
            return;
        }

        switch (m.Kind)
        {
            case ShellMessageKind.Shell:
                _shellUp = true;
                foreach (var js in _queued)
                {
                    Post(js);
                }

                _queued.Clear();
                break;
            case ShellMessageKind.Ready:
                Ready(m.Sequence);
                break;
            case ShellMessageKind.Key when m.Key is not null && ShellMessageParser.ParseKey(m.Key) is { } key:
                _ = _keys.Value.Handle(ForwardedKeyChords.ToChord(key), KeySource.WebView);
                break;
            case ShellMessageKind.Link when m.Link is not null:
                RoutedLinks++;
                _links.Route(m.Link);
                break;
            default:
                // dirty: nothing to refresh (no capture under the view on Windows); ctx: the context menu is M2's
                break;
        }
    }

    private void SetSlow(bool on)
    {
        if (_slow != on)
        {
            _slow = on;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StopReadyTimer()
    {
        _readyTimer?.Dispose();
        _readyTimer = null;
    }

    // One command to the shell: a web message on Windows (one ordered channel for every command, large pages included), a script call on Linux.
    private void Command(string op, long seq = 0, string? html = null, string? bg = null, string? sandbox = null, bool on = false)
    {
#if WINDOWS
        var buffer = new ArrayBufferWriter<byte>(html is null ? 128 : html.Length + 256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("op", op);
            w.WriteNumber("seq", seq);
            if (html is not null)
            {
                w.WriteString("html", html);
            }

            if (bg is not null)
            {
                w.WriteString("bg", bg);
            }

            if (sandbox is not null)
            {
                w.WriteString("sandbox", sandbox);
            }

            w.WriteBoolean("on", on);
            w.WriteEndObject();
        }

        Post(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan));
#else
        var s = seq.ToString(CultureInfo.InvariantCulture);
        var js = op switch
        {
            "show" => $"window.__bb&&__bb.show({s},{Json(html!)},{Json(bg!)},{Json(sandbox!)})",
            "reveal" => $"window.__bb&&__bb.reveal({s})",
            "cancel" => $"window.__bb&&__bb.cancel({s})",
            "dim" => $"window.__bb&&__bb.dim({(on ? "true" : "false")})",
            "clear" => $"window.__bb&&__bb.clear({Json(bg!)})",
            _ => "void 0",
        };
        Post(js);
#endif
    }

    // a payload starting with { is a web message (Windows), anything else a script
    private void Post(string payload)
    {
        if (!_shellUp || _view?.CoreWebView2 is null)
        {
            _queued.Add(payload);
            return;
        }

#if WINDOWS
        if (payload.StartsWith('{'))
        {
            _view.CoreWebView2.PostWebMessageAsJson(payload);
            return;
        }
#endif
        _ = _view.ExecuteScriptAsync(payload);
    }

#if !WINDOWS
    // a JSON string is a JavaScript string literal (the default encoder also escapes <, > and &)
    private static string Json(string s) => "\"" + JsonEncodedText.Encode(s).ToString() + "\"";
#endif

    [LoggerMessage(Level = LogLevel.Error, Message = "the mail body view could not start")]
    private static partial void LogStartFailed(ILogger logger, Exception e);

    [LoggerMessage(Level = LogLevel.Debug, Message = "a WebView2 feature is not available here")]
    private static partial void LogSettingFailed(ILogger logger, Exception e);
}
