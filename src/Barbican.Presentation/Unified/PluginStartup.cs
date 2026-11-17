// Mirrors: Rust main.rs:455 load_plugins (at start, on a background thread; disabledPlugins from the prefs). D-234 (the bundled spam-score
// plugin is loaded by the host, never skipped). Owner unified.

using Barbican.Core.Plugins.Native;
using Barbican.Presentation.Composition;

namespace Barbican.Presentation.Unified;

/// <summary>Loads the native plugins once the app has started (the window never waits for it).</summary>
public sealed class PluginStartup(NativePlugins plugins) : IStartupTask
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = plugins.LoadAsync();
        return Task.CompletedTask;
    }
}
