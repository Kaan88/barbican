// Mirrors: Rust plugins.rs:29 Plugin (id, name, version, description, hooks, path; handles(hook)), :127 describe; M2 plan 4.6 (INativePluginHost.Plugins).
// Owner unified (M2 wave 2).

namespace Barbican.Core.Plugins.Native;

/// <summary>A loaded native plugin as the host lists it.</summary>
/// <param name="Id">The manifest id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Version">The plugin's version.</param>
/// <param name="Hooks">The hook names it handles.</param>
public sealed record PluginInfo(string Id, string Name, string Version, IReadOnlyList<string> Hooks)
{
    /// <summary>The manifest's one-sentence description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The folder it was loaded from.</summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>False when the id is in the prefs' disabledPlugins (Rust main.rs:455): listed, but never called.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>True for a plugin that ships with the app (the bundled folder), false for one the user added.</summary>
    public bool Bundled { get; init; }

    /// <summary>Whether the plugin handles a hook name ("email.details").</summary>
    public bool Handles(string hook) => Hooks.Contains(hook, StringComparer.Ordinal);
}
