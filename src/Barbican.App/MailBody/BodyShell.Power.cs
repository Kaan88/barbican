// Mirrors: Microsoft WebView2 API reference: CoreWebView2.TrySuspendAsync / Resume / IsSuspended, CoreWebView2.MemoryUsageTargetLevel,
// WebView2.Close(); decision D-WV-01 (spec/decisions.md). The App adapter of Presentation's BodyBackgroundPolicy: it does the WebView2 calls.
// Windows only; the Uno/Linux head (WebKitGTK through Uno's WebView2) has none of these members and reports IsSupported false, so the
// policy is a no-op there (Linux pending).

using Barbican.Presentation.Mail.Loading;

namespace Barbican.App.MailBody;

public sealed partial class BodyShell : IBodyWebViewHost
{
#pragma warning disable CS0649, CS0067, IDE0044 // written only by the Windows adapter below; the Linux head never closes its view
    private bool _closed;
    private bool _recreating;
    private BodyRequest? _restore;

    /// <summary>How the last TrySuspendAsync ended (automation: ok, refused, or the exception), or null.</summary>
    public string? LastSuspend { get; private set; }

    /// <summary>The WebView was closed or created again: the body view puts the new one in its tree.</summary>
    public event EventHandler? ViewReplaced;
#pragma warning restore CS0649, CS0067, IDE0044

#if WINDOWS
    bool IBodyWebViewHost.IsSupported => true;
#else
    bool IBodyWebViewHost.IsSupported => false;
#endif

    bool IBodyWebViewHost.IsCreated => _view is not null && !_closed;

    bool IBodyWebViewHost.HasContent => _restore is not null || _current is not null;

#if WINDOWS
    void IBodyWebViewHost.Hide()
    {
        if (_view is not { } v)
        {
            return;
        }

        // the control's Visibility drives CoreWebView2Controller.IsVisible; a suspend needs it false
        v.Visibility = Visibility.Collapsed;
        if (v.CoreWebView2 is { } core)
        {
            Try(() => core.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low);
        }
    }

    async Task<bool> IBodyWebViewHost.SuspendAsync()
    {
        if (_view?.CoreWebView2 is not { } core || core.IsSuspended)
        {
            return false;
        }

        // the control passes Visibility to CoreWebView2Controller.IsVisible on its next layout pass; TrySuspendAsync fails with
        // ERROR_INVALID_STATE until that happened, so a refused call is tried again shortly (at most 5 times)
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (_view?.CoreWebView2 is not { } live || live.IsSuspended)
            {
                return false;
            }

            try
            {
                var ok = await live.TrySuspendAsync();
                LastSuspend = ok ? "ok" : "refused";
                return ok;
            }
#pragma warning disable CA1031 // TrySuspendAsync throws when the view is not hidden yet or is mid-navigation: retried, then reported, never fatal
            catch (Exception e)
#pragma warning restore CA1031
            {
                LastSuspend = e.GetType().Name + ": " + e.Message;
                LogSettingFailed(_log, e);
            }

            _view?.UpdateLayout();
            await Task.Delay(100);
            if (_view?.CoreWebView2 is null || _view.Visibility != Visibility.Collapsed)
            {
                return false;
            }
        }

        return false;
    }

    void IBodyWebViewHost.ResumeView()
    {
        if (_view?.CoreWebView2 is not { } core)
        {
            return;
        }

        if (core.IsSuspended)
        {
            core.Resume();
        }

        Try(() => core.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Normal);
    }

    void IBodyWebViewHost.Show()
    {
        if (_view is { } v)
        {
            v.Visibility = Visibility.Visible;
        }
    }

    void IBodyWebViewHost.Close()
    {
        if (_view is not { } v)
        {
            return;
        }

        _restore = (_pending?.Request) ?? _current?.Request;
        StopReadyTimer();
        SetSlow(false);
        _pending = null;
        _current = null;
        _closed = true;
        _shellUp = false;
        _shellNavigated = false;
        _starting = false;
        _queued.Clear();
        if (v.Parent is Panel parent)
        {
            parent.Children.Remove(v);
        }

        try
        {
            v.Close();
        }
#pragma warning disable CA1031 // a view that is already gone is closed enough
        catch (Exception e)
#pragma warning restore CA1031
        {
            LogSettingFailed(_log, e);
        }

        _view = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    void IBodyWebViewHost.Recreate()
    {
        _recreating = true;
        try
        {
            _closed = false;
            _ = View;
            ViewReplaced?.Invoke(this, EventArgs.Empty);
            if (_restore is { } r)
            {
                _restore = null;
                // sent to the new shell once it is up (queued until then); no callbacks: the loader's state is unchanged
                Show(r with { OnReady = static () => true, OnShown = null });
            }
        }
        finally
        {
            _recreating = false;
        }
    }
#else
    void IBodyWebViewHost.Hide()
    {
    }

    Task<bool> IBodyWebViewHost.SuspendAsync() => Task.FromResult(false);

    void IBodyWebViewHost.ResumeView()
    {
    }

    void IBodyWebViewHost.Show()
    {
    }

    void IBodyWebViewHost.Close()
    {
    }

    void IBodyWebViewHost.Recreate()
    {
    }
#endif
}
