// Mirrors: Bulwark lib/plugin-types.ts (the context a plugin gets: its storage, api.hooks); M2 plan 3.2. Owner unified (M2 wave 2).

namespace Barbican.Plugins;

/// <summary>What the host gives a plugin when it starts it.</summary>
/// <param name="PluginDirectory">The folder the plugin was loaded from (read-only for the plugin).</param>
/// <param name="DataDirectory">A folder the plugin may write its own data to.</param>
public sealed record PluginContext(string PluginDirectory, string DataDirectory)
{
    /// <summary>Subscribes to catalogue hooks the manifest lists (none outside the host).</summary>
    public IPluginHooks Hooks { get; init; } = NoPluginHooks.Instance;
}
