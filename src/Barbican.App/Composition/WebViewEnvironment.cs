using Barbican.Core.Storage;
#if WINDOWS
using Microsoft.Web.WebView2.Core;
#endif

namespace Barbican.App.Composition;

/// <summary>
/// WebView settings applied explicitly before navigation, without a process-wide data-folder override:
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

    public static void Prepare(HiddenWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.IsHidden && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ProfilePaths.Variable)))
        {
            Environment.SetEnvironmentVariable(ProfilePaths.Variable, options.Profile);
        }
    }

#if WINDOWS
    /// <summary>Creates a Windows environment for a host-selected absolute folder, verifying loader policy did not redirect it.</summary>
    public static async Task<CoreWebView2Environment> CreateWindowsAsync(string folder, bool hidden, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException("WebView2 data folder must be absolute.", nameof(folder));
        }

        var expected = Path.GetFullPath(folder);
        if (Environment.GetEnvironmentVariable(UserDataVariable) is { Length: > 0 } overridden)
        {
            ValidateFolder(expected, overridden);
        }

        ct.ThrowIfCancellationRequested();
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = Arguments(hidden) };
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, expected, options);
        ct.ThrowIfCancellationRequested();
        ValidateFolder(expected, environment.UserDataFolder);
        return environment;
    }
#endif

    internal static void ValidateFolder(string expected, string actual)
    {
        if (!Path.IsPathFullyQualified(actual) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("WebView2 selected a different data folder; startup was refused.");
        }
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
