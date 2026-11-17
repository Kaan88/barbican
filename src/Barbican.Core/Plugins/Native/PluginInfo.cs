// Mirrors: Rust plugins.rs (the loaded Plugin: id, handles(hook)); M2 plan 4.6 (INativePluginHost.Plugins). Stub from stage F of M2; owner unified.

namespace Barbican.Core.Plugins.Native;

/// <summary>A loaded native plugin as the host lists it.</summary>
/// <param name="Id">The manifest id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Version">The plugin's version.</param>
/// <param name="Hooks">The hook names it handles.</param>
public sealed record PluginInfo(string Id, string Name, string Version, IReadOnlyList<string> Hooks)
{
    /// <summary>Whether the plugin handles a hook name ("email.details").</summary>
    public bool Handles(string hook) => Hooks.Contains(hook, StringComparer.Ordinal);
}
