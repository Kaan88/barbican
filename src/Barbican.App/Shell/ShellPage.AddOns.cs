#if WINDOWS
using Barbican.App.Composition;
using Barbican.App.Plugins;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Native;
using Barbican.Core.Storage;
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
        var views = hasBootstrap ? new PluginSandboxWindowsViewFactory(DispatcherQueue, () => AddOnRuntimeMount, paths, options) : null;
        var startup = hasBootstrap ? new PluginSandboxWindowsStartup(bootstrap, ViewHost.Get<ILogger<PluginSandboxWindowsStartup>>()) : null;
        var native = ViewHost.Get<NativePlugins>();
        _ = ViewHost.Get<PluginBackgroundRuntimes>().InitializeAsync(
            ct => packages.CreateAddOnFactoryAsync(views, startup, hooks, sessions, methods, includeUserStorage: true, native, ct),
            CancellationToken.None, javaScriptAvailable: hasBootstrap);
    }
}
#endif
