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
        var bootstrap = Path.Combine(AppContext.BaseDirectory, "Assets", "plugin-sandbox-bootstrap");
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
        _ = ViewHost.Get<PluginBackgroundRuntimes>().InitializeAsync(
            ct => packages.CreateAddOnFactoryAsync(views, startup, hooks, sessions, methods, includeUserStorage: true, native, oauthBrowser, prefs, ct),
            CancellationToken.None, javaScriptAvailable: hasBootstrap);
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
