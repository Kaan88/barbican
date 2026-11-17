// Mirrors: Rust plugins.rs:29 Plugin (the loaded library kept with its manifest); M2 plan 6.5 (a collectible AssemblyLoadContext per plugin). Owner unified.

using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>One plugin the host holds: what it lists, the instance, its load context, its hook subscriptions and its start.</summary>
internal sealed class LoadedPlugin(PluginInfo info, IBarbicanPlugin plugin, PluginLoadContext? context)
{
    public PluginInfo Info { get; } = info;

    public IBarbicanPlugin Plugin { get; } = plugin;

    /// <summary>The plugin's own load context; null for an in-process plugin (tests).</summary>
    public PluginLoadContext? Context { get; } = context;

    /// <summary>The plugin's hook subscriptions on the app's bus (set right after loading).</summary>
    public PluginHookBridge Bridge { get; set; } = null!;

    /// <summary>Completes when InitializeAsync returned; faulted when the plugin could not start (it is then never called).</summary>
    public Task Ready { get; set; } = Task.CompletedTask;
}
