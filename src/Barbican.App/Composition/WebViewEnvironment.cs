using Barbican.Core.Storage;

namespace Barbican.App.Composition;

/// <summary>
/// WebView2 settings applied before any WebView exists (read from the environment when the first CoreWebView2 environment is created):
/// <list type="bullet">
/// <item>every run (Fable review 1 F-13): its own user-data folder under the profile's data folder
/// (<c>%LOCALAPPDATA%\Barbican-PROFILE\WebView2</c>), never the folder next to the executable (read-only when installed, shared by all
/// profiles of the portable zip) and never the user's or the Rust app's; and no DNS prefetching (a mail must not resolve the names it
/// links to; F-03);</item>
/// <item>hidden runs only: Chromium's native window occlusion tracking off (an off-screen window counts as occluded and would stop
/// painting, so captures of the body would go stale: spike S2/S4) and no background timer throttling.</item>
/// </list>
/// A hidden run without BARBICAN_PROFILE gets its own profile name in the environment first (auto-PORT or smoke), so Core's profile
/// folders and the WebView2 folder are the run's own and never the dev profile's. The Linux head (WebKitGTK) reads none of this; its data
/// directory is Linux pending.
/// </summary>
internal static class WebViewEnvironment
{
    public const string UserDataVariable = "WEBVIEW2_USER_DATA_FOLDER";
    public const string ArgumentsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";

    public static void Prepare(HiddenWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.IsHidden && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ProfilePaths.Variable)))
        {
            Environment.SetEnvironmentVariable(ProfilePaths.Variable, options.Profile);
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(UserDataVariable)))
        {
            Environment.SetEnvironmentVariable(UserDataVariable, UserDataFolder(ProfilePaths.FromEnvironment()));
        }

        Environment.SetEnvironmentVariable(ArgumentsVariable, Arguments(options.IsHidden));
    }

    /// <summary>The WebView2 user-data folder of a profile.</summary>
    public static string UserDataFolder(IProfilePaths profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Path.Combine(profile.DataDir, "WebView2");
    }

    /// <summary>The browser arguments of a run.</summary>
    public static string Arguments(bool hidden) =>
        hidden
            ? "--dns-prefetch-disable --disable-features=CalculateNativeWinOcclusion --disable-background-timer-throttling"
            : "--dns-prefetch-disable";
}
