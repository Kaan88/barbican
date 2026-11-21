#if WINDOWS
using Barbican.App.Composition;
using Barbican.App.Plugins;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Native;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Barbican.Platform;
using Barbican.Presentation.Dialogs;
using Microsoft.Extensions.Logging;

namespace Barbican.App.Shell;

public sealed partial class ShellPage
{
    private void StartAddOnRuntimes()
    {
        var bootstrap = BootstrapRoot();
        var hasBootstrap = Directory.Exists(bootstrap);

        var packages = ViewHost.Get<PluginPackageHost>();
        var paths = ViewHost.Get<IProfilePaths>();
        var options = ViewHost.Get<HiddenWindowOptions>();
        var hooks = ViewHost.Get<HookBus>();
        var sessions = ViewHost.Get<IPluginSessionSource>();
        var methods = ViewHost.Get<IEnumerable<IPluginApiMethod>>();
        var prefs = ViewHost.Get<IPrefsStore>();
        var views = hasBootstrap ? new PluginSandboxWindowsViewFactory(DispatcherQueue, () => AddOnRuntimeMount, paths, options) : null;
        var startup = hasBootstrap ? new PluginSandboxWindowsStartup(bootstrap, ViewHost.Get<ILogger<PluginSandboxWindowsStartup>>()) : null;
        var native = ViewHost.Get<NativePlugins>();
        var oauthBrowser = new PluginPackageHost.PluginOAuthBrowser(
            new BrowserAuthorizationLauncher(ViewHost.Get<IBrowserLauncher>()), ConfirmAddOnOAuthAsync);
        if (hasBootstrap)
        {
            // Visible slot frames (add-on pages) take their own views, mounted by the screen that shows them, never the hidden mount.
            var slotViews = new PluginSandboxWindowsViewFactory(DispatcherQueue, PluginSlotMount.Get, paths, options, interactive: true);
            ViewHost.Get<PluginSlotFrames>().Initialize(
                ct => packages.CreateAddOnFactoryAsync(slotViews, startup, hooks, sessions, methods, includeUserStorage: true, native, oauthBrowser, prefs, ct));
        }
        _ = ViewHost.Get<PluginBackgroundRuntimes>().InitializeAsync(
            ct => packages.CreateAddOnFactoryAsync(views, startup, hooks, sessions, methods, includeUserStorage: true, native, oauthBrowser, prefs, ct),
            CancellationToken.None, javaScriptAvailable: hasBootstrap);
    }

    // The pinned sandbox bootstrap is not part of the repository: the portable package carries it in Assets/plugin-sandbox-bootstrap (tools/package-portable.ps1 -BootstrapDirectory).
    // An AUTOMATION build of a synthetic automation profile may take it from BARBICAN_BOOTSTRAP_DIR instead (an absolute folder, say the extracted pinned runtime), so a hidden run can draw add-on frames
    // without copying those files into the build output. Every file is still checked against its pinned size and SHA-256 by PluginSandboxBootstrapAssets before anything runs.
    private static string BootstrapRoot()
    {
#if AUTOMATION
        var supplied = Environment.GetEnvironmentVariable("BARBICAN_BOOTSTRAP_DIR");
        if (!string.IsNullOrEmpty(supplied) && Path.IsPathFullyQualified(supplied) && ProfilePaths.IsAutomationProfile(ViewHost.Get<IProfilePaths>().Profile)) return supplied;
#endif
        return Path.Combine(AppContext.BaseDirectory, "Assets", "plugin-sandbox-bootstrap");
    }

    private async Task<bool> ConfirmAddOnOAuthAsync(PluginRuntimeBinding binding, Uri provider, CancellationToken ct)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = provider.IsDefaultPort ? provider.IdnHost : provider.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped);
        if (!DispatcherQueue.TryEnqueue(() => _ = AskAsync())) return false;
        return await result.Task.WaitAsync(ct).ConfigureAwait(false);

        async Task AskAsync()
        {
            try
            {
                var dialogs = ViewHost.Find<IDialogService>();
                if (ct.IsCancellationRequested || !binding.IsAlive || dialogs is null)
                {
                    result.TrySetResult(false);
                    return;
                }
                var request = new ConfirmDialogRequest("Open add-on sign-in",
                    $"Allow {binding.Package.Id} to open sign-in at {host} in your browser?", "Open browser");
                result.TrySetResult(await dialogs.ShowAsync(request, ct).ConfigureAwait(true));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { result.TrySetCanceled(ct); }
            catch (InvalidOperationException) { result.TrySetResult(false); }
        }
    }
}
#endif
