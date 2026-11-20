#if WINDOWS
using Barbican.App.Composition;
using Barbican.App.Plugins;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Barbican.App.Shell;

public sealed partial class ShellPage
{
    private void StartAddOnRuntimes()
    {
        var bootstrap = Path.Combine(AppContext.BaseDirectory, "Assets", "plugin-sandbox-bootstrap");
        if (!Directory.Exists(bootstrap)) return;

        var packages = ViewHost.Get<PluginPackageHost>();
        var paths = ViewHost.Get<IProfilePaths>();
        var options = ViewHost.Get<HiddenWindowOptions>();
        var hooks = ViewHost.Get<HookBus>();
        var sessions = ViewHost.Get<IPluginSessionSource>();
        var methods = ViewHost.Get<IEnumerable<IPluginApiMethod>>();
        var views = new PluginSandboxWindowsViewFactory(DispatcherQueue, () => AddOnRuntimeMount, paths, options);
        var startup = new PluginSandboxWindowsStartup(bootstrap, ViewHost.Get<ILogger<PluginSandboxWindowsStartup>>());
        _ = ViewHost.Get<PluginBackgroundRuntimes>().InitializeAsync(
            ct => packages.CreateSandboxFactoryAsync(views, startup, hooks, sessions, methods, ct), CancellationToken.None);
    }
}
#endif
