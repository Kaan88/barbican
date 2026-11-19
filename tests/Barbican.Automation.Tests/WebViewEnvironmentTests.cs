using Barbican.App.Composition;
using Barbican.Core.Storage;

namespace Barbican.Automation.Tests;

/// <summary>
/// Fable review 1 F-13: every run, not only hidden ones, gets the profile's own WebView2 user-data folder and no DNS prefetching; a hidden
/// run without BARBICAN_PROFILE gets its own profile so it never uses the dev profile's folders.
/// </summary>
public sealed class WebViewEnvironmentTests
{
    [Fact]
    public void The_user_data_folder_is_under_the_profiles_data_folder()
    {
        var profile = ProfilePaths.At("dev", Path.Combine("c", "Barbican-dev"), Path.Combine("d", "Barbican-dev"));
        Assert.Equal(Path.Combine("d", "Barbican-dev", "WebView2"), WebViewEnvironment.UserDataFolder(profile));
    }

    [Fact]
    public void Every_run_turns_dns_prefetch_off_and_only_hidden_runs_change_painting()
    {
        Assert.Equal("--dns-prefetch-disable", WebViewEnvironment.Arguments(hidden: false));
        Assert.StartsWith("--dns-prefetch-disable ", WebViewEnvironment.Arguments(hidden: true), StringComparison.Ordinal);
        Assert.Contains("CalculateNativeWinOcclusion", WebViewEnvironment.Arguments(hidden: true), StringComparison.Ordinal);
    }

    [Fact]
    public void A_normal_run_gets_its_profiles_folder_and_a_hidden_run_its_own_profile()
    {
        string[] names = [ProfilePaths.Variable, WebViewEnvironment.UserDataVariable];
        var saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (var n in names)
            {
                Environment.SetEnvironmentVariable(n, null);
            }

            var normal = new HiddenWindowOptions(HiddenWindowMode.Normal, null, 0, "default", null);
            WebViewEnvironment.Prepare(normal);
            Assert.Null(Environment.GetEnvironmentVariable(ProfilePaths.Variable));
            Assert.Null(Environment.GetEnvironmentVariable(WebViewEnvironment.UserDataVariable));
            Assert.EndsWith(Path.Combine("Barbican-dev", "WebView2"), WebViewEnvironment.UserDataFolder(ProfilePaths.FromEnvironment()), StringComparison.Ordinal);

            var hidden = new HiddenWindowOptions(HiddenWindowMode.OffScreen, null, 8899, "auto-8899", null);
            var externalFolder = Path.GetFullPath("external-webview");
            Environment.SetEnvironmentVariable(WebViewEnvironment.UserDataVariable, externalFolder);
            WebViewEnvironment.Prepare(hidden);
            Assert.Equal("auto-8899", Environment.GetEnvironmentVariable(ProfilePaths.Variable));
            Assert.Equal(externalFolder, Environment.GetEnvironmentVariable(WebViewEnvironment.UserDataVariable));
            Assert.EndsWith(Path.Combine("Barbican-auto-8899", "WebView2"), WebViewEnvironment.UserDataFolder(ProfilePaths.FromEnvironment()), StringComparison.Ordinal);
        }
        finally
        {
            foreach (var (n, v) in saved)
            {
                Environment.SetEnvironmentVariable(n, v);
            }
        }
    }

    [Fact]
    public void A_loader_override_must_match_the_requested_folder()
    {
        var expected = Path.GetFullPath("isolated-webview");
        WebViewEnvironment.ValidateFolder(expected, expected + Path.DirectorySeparatorChar);
        Assert.Throws<InvalidOperationException>(() => WebViewEnvironment.ValidateFolder(expected, Path.GetFullPath("other-webview")));
        Assert.Throws<InvalidOperationException>(() => WebViewEnvironment.ValidateFolder(expected, "isolated-webview"));
    }
}
