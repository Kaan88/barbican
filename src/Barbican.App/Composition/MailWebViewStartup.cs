using Barbican.Core.Storage;

namespace Barbican.App.Composition;

/// <summary>Initializes the reader and smoke controls before they navigate to mail content.</summary>
internal static class MailWebViewStartup
{
    public static async Task EnsureAsync(WebView2 view, bool hidden)
    {
        ArgumentNullException.ThrowIfNull(view);
#if WINDOWS
        var folder = WebViewEnvironment.UserDataFolder(ProfilePaths.FromEnvironment());
        var environment = await WebViewEnvironment.CreateWindowsAsync(folder, hidden, CancellationToken.None);
        await view.EnsureCoreWebView2Async(environment);
        WebViewEnvironment.ValidateFolder(folder, view.CoreWebView2.Environment.UserDataFolder);
#else
        await view.EnsureCoreWebView2Async();
#endif
    }
}
