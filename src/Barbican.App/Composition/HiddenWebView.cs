namespace Barbican.App.Composition;

/// <summary>
/// WebView2 settings of a hidden run, applied before any WebView exists (they are read from the
/// environment when the first CoreWebView2 environment is created):
/// <list type="bullet">
/// <item>its own user-data folder under the run's profile (<c>%LOCALAPPDATA%\Barbican-PROFILE\WebView2</c>),
/// never the user's or the Rust app's;</item>
/// <item>Chromium's native window occlusion tracking off: an off-screen window counts as occluded and
/// would stop painting, so captures of the body would go stale (spike S2/S4 finding).</item>
/// </list>
/// The Linux head (WebKitGTK) reads neither variable.
/// </summary>
internal static class HiddenWebView
{
    public static void Prepare(HiddenWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")))
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(root, "Barbican-" + options.Profile, "WebView2"));
        }

        Environment.SetEnvironmentVariable(
            "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
            "--disable-features=CalculateNativeWinOcclusion --disable-background-timer-throttling");
    }
}
