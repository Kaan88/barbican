// Mirrors: docs/adr/0005 (the one warm body WebView2); Microsoft WebView2 API reference: CoreWebView2.TrySuspendAsync / Resume / IsSuspended
// (learn.microsoft.com/microsoft-edge/webview2/reference/win32/icorewebview2_3), CoreWebView2.MemoryUsageTargetLevel
// (.../icorewebview2_19), CoreWebView2Controller.IsVisible, WebView2.Close(). Decision D-WV-01.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>What the background policy does to the mail body's web view. Every member runs on the UI thread and must be safe to call in any state.</summary>
public interface IBodyWebViewHost
{
    /// <summary>The platform can release a web view's resources (WebView2 on Windows). False: the policy does nothing.</summary>
    bool IsSupported { get; }

    /// <summary>The web view exists (it is created on first use and again after a close).</summary>
    bool IsCreated { get; }

    /// <summary>There is a message on screen that a re-created web view must show again.</summary>
    bool HasContent { get; }

    /// <summary>Hides the web view (IsVisible false; a suspend needs it) and lowers its memory target (MemoryUsageTargetLevel Low).</summary>
    void Hide();

    /// <summary>TrySuspendAsync on the hidden web view; false when the platform refused or it was not created.</summary>
    Task<bool> SuspendAsync();

    /// <summary>Resumes a suspended web view and restores the normal memory target (a no-op when it is not suspended or not created).</summary>
    void ResumeView();

    /// <summary>Makes the web view visible again.</summary>
    void Show();

    /// <summary>Closes the web view and its browser processes; the shown message is kept to be shown again on a re-create.</summary>
    void Close();

    /// <summary>Creates the web view again (the shown message, if any, is sent to it once the shell is up).</summary>
    void Recreate();
}
