// Mirrors: Rust mailview.rs (tag rust-final): SHELL_JS :51 (now Assets/mail/shell.js), shell_html :127 (Core ShellDocument), sandbox :140
// (Core MailSandbox: never allow-scripts, Fable F-03), show :158, slow :178, cancel_pending :190, dim :199, ready :206, theme_changed :232, clear :240, on_ipc :326, build :369
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
// Fable review 2 R2-04: the shell reveals only a frame that passed its finish() (shell.js), so the give-up path no longer shows a page the
// shell never handled (no link handling, no dark pass, perhaps a remote page on Linux): when a page is not ready in time the error page is
// sent in its place, and only when that is not ready either does the wait end without a body (the old page stays).

#if WINDOWS
using System.Buffers;
#else
using System.Globalization;
#endif
using System.Text.Json;
using Barbican.App.Theme;
using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Reader.Hooks;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
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
    private readonly IHookBus _hooks;
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
    private (long Seq, BodyRequest Request, MailPage Page, bool Allow, bool Fallback)? _pending;
    private (BodyRequest Request, MailPage Page, bool Allow)? _current;
    private ITimer? _readyTimer;
    private bool _slow;
    private bool _loaderLoading;
    private string? _allowRemoteFor;
    private string? _formattedFor;

    public BodyShell(BodyAppearance appearance, Lazy<IKeyRouter> keys, ILinkRouter links, IDispatcher dispatcher, TimeProvider time, ThemeController theme, ILogger<BodyShell> log, IHookBus hooks, IPrefsStore prefs)
    {
        Power = new BodyBackgroundPolicy(this, time, dispatcher, () => BodyBackgroundPolicy.ParseMinutes(prefs.Current.Str(BodyBackgroundPolicy.SettingKey, "5")));
        _hooks = hooks;
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

    /// <summary>The background policy (D-WV-01): the window's visibility calls it; see BodyShell.Power.cs.</summary>
    public BodyBackgroundPolicy Power { get; }

    /// <summary>The shell document is up.</summary>
    public bool IsShellUp => _shellUp;

    /// <summary>The number of the last document sent.</summary>
    public long DocumentSequence => _docSeq;

    /// <summary>The message on screen, or null.</summary>
    public string? ShownId => _current?.Request.EmailId;

    /// <summary>The loading bar is on (the loader's slow state or a slow page).</summary>
    public bool IsLoading => _loaderLoading || _slow;

    /// <summary>The message on screen has remote content that is blocked and may be loaded ("Load external content").</summary>
    public bool RemoteBlocked => _current is { } c && c.Page.HasRemoteContent && !c.Allow && !c.Request.BlockRemote;

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
        if (!_recreating)
        {
            // a suspended web view is resumed and a closed one created again before anything is sent to it (D-WV-01)
            Power.BeforeUse();
        }

        if (!string.Equals(_allowRemoteFor, request.EmailId, StringComparison.Ordinal))
        {
            _allowRemoteFor = null;
        }

        if (!string.Equals(_formattedFor, request.EmailId, StringComparison.Ordinal))
        {
            _formattedFor = null;
        }

        var allow = !request.BlockRemote && (request.TrustedSender || string.Equals(_allowRemoteFor, request.EmailId, StringComparison.Ordinal));
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
        _restore = null;
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
        Power.Dispose();
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
            // a message frame may only ever hold its srcdoc (a meta refresh or a script-free self navigation would load a remote page,
            // or about:blank would replace the message): everything else is cancelled (Fable F-03)
            if (!string.Equals(e.Uri, "about:srcdoc", StringComparison.OrdinalIgnoreCase))
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
            if (remote && !RemoteAllowed)
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

    // The request filter lets a remote load through only while a page that may load remote content is alive in the shell (the one on
    // screen or the one loading); each page keeps its own permission, so the banner of the page on screen never reads the next page's.
    private bool RemoteAllowed => (_current?.Allow ?? false) || (_pending?.Allow ?? false);

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

    /// <summary>The text of the page sent instead of a message the shell could not finish in time.</summary>
    public const string NotShownText = "This message could not be shown.";

    private void Send(BodyRequest request, MailPage page, bool allowRemote, bool fallback = false)
    {
        var seq = ++_docSeq;
        _pending = (seq, request, page, allowRemote, fallback);
        _sentAt = _time.GetTimestamp();
        StopReadyTimer();
        _readyTimer = _time.CreateTimer(_ => _dispatcher.Post(() => Slow(seq)), null, SlowAfter, Timeout.InfiniteTimeSpan);

        Command("show", seq, html: page.Html, bg: page.Background, sandbox: MailSandbox.Frame, page: page);
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
        _readyTimer = _time.CreateTimer(_ => _dispatcher.Post(() => GiveUp(seq)), null, GiveUpAfter, Timeout.InfiniteTimeSpan);
    }

    // Document seq never passed the shell's finish() (R2-04): the shell would refuse to reveal it, so the error page goes in its place; if even
    // that is not ready, the native side is told and the reveal is refused by the shell (the page on screen stays).
    private void GiveUp(long seq)
    {
        if (_pending is not { } p || p.Seq != seq || seq != _docSeq)
        {
            return;
        }

        if (p.Fallback)
        {
            Ready(seq);
            return;
        }

        Send(p.Request, MailPageBuilder.Error(NotShownText, _appearance.Options(false)), allowRemote: false, fallback: true);
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

        _current = (p.Request, p.Page, p.Allow);
        PaintHost(p.Page.Background);
        // the body cross-fades in on the view token, never while keys repeat (design/MOTION.md rules 2 and 4)
        var scale = Motion.MotionRuntime.Scale;
        Command("reveal", seq, fadeMs: scale.Rapid ? 0 : scale.ViewMs);
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
            case ShellMessageKind.Selection when m.Text is not null && _current is { } shown && _hooks.HasHandlers(HookNames.OnTextSelectionChange):
                _ = _hooks.NotifyAsync(HookNames.OnTextSelectionChange, new SelectionContext(shown.Request.EmailId, m.Text), CancellationToken.None).AsTask();
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
    private void Command(string op, long seq = 0, string? html = null, string? bg = null, string? sandbox = null, bool on = false, MailPage? page = null, double fadeMs = 0)
    {
#if WINDOWS
        var buffer = new ArrayBufferWriter<byte>(html is null ? 128 : html.Length + 256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("op", op);
            w.WriteNumber("seq", seq);
            if (fadeMs > 0)
            {
                w.WriteNumber("ms", Math.Round(fadeMs));
            }

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

            if (page is not null)
            {
                // the shell runs these on the frame document (no script runs in the frame, Fable F-03)
                w.WriteBoolean("dark", page.Dark);
                if (page.Sizing is { } z)
                {
                    w.WriteStartArray("size");
                    w.WriteNumberValue(z.Factor);
                    w.WriteNumberValue(z.MinPx);
                    w.WriteBooleanValue(z.Fit);
                    w.WriteEndArray();
                }
            }

            w.WriteBoolean("on", on);
            w.WriteEndObject();
        }

        Post(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan));
#else
        var s = seq.ToString(CultureInfo.InvariantCulture);
        var js = op switch
        {
            "show" => $"window.__bb&&__bb.show({s},{Json(html!)},{Json(bg!)},{Json(sandbox!)},{(page?.Dark == true ? "true" : "false")},{SizeJs(page?.Sizing)})",
            "reveal" => $"window.__bb&&__bb.reveal({s},{Math.Round(fadeMs).ToString(CultureInfo.InvariantCulture)})",
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
        if (_closed)
        {
            // the web view is closed (background release): the shown message is kept in _restore and sent again on re-create
            return;
        }

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

    // the sizing as a JavaScript array literal, or null
    private static string SizeJs(Barbican.Core.Settings.Accessibility.MailSizing? z) => z is null
        ? "null"
        : string.Create(CultureInfo.InvariantCulture, $"[{z.Factor},{z.MinPx},{(z.Fit ? "true" : "false")}]");
#endif

    [LoggerMessage(Level = LogLevel.Error, Message = "the mail body view could not start")]
    private static partial void LogStartFailed(ILogger logger, Exception e);

    [LoggerMessage(Level = LogLevel.Debug, Message = "a WebView2 feature is not available here")]
    private static partial void LogSettingFailed(ILogger logger, Exception e);
}
