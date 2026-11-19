#if WINDOWS
// WebView2 WinRT environment API: CreateWithOptionsAsync and UserDataFolder; see M6b-runtime-transport.md.
using Microsoft.Web.WebView2.Core;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Creates an isolated plugin environment and rejects loader policy overrides before a control is created.</summary>
internal static class PluginSandboxWindowsEnvironment
{
    public static async Task<CoreWebView2Environment> CreateAsync(string userDataFolder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataFolder);
        if (!Path.IsPathFullyQualified(userDataFolder)) throw new ArgumentException("Plugin WebView folder must be absolute.", nameof(userDataFolder));
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDataFolder));
        // Do not ask the loader to touch a known different profile. Registry overrides
        // cannot be read reliably here: compare the actual environment afterwards too.
        var overrideFolder = Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER");
        if (!string.IsNullOrWhiteSpace(overrideFolder) && !SameFolder(expected, overrideFolder))
            throw new InvalidOperationException("WebView2 data-folder override prevents plugin isolation.");
        ct.ThrowIfCancellationRequested();
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, expected, null);
        ct.ThrowIfCancellationRequested();
        if (!SameFolder(expected, environment.UserDataFolder))
            throw new InvalidOperationException("WebView2 selected a different data folder; plugin startup was refused.");
        return environment;
    }
    private static bool SameFolder(string expected, string actual) =>
        Path.IsPathFullyQualified(actual) && string.Equals(expected, Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)), StringComparison.OrdinalIgnoreCase);
}
#endif
