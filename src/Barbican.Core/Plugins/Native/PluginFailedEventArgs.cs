// Mirrors: Bulwark lib/plugin-hooks.ts:24-57 pluginErrorTracker (a failing plugin is recorded, never thrown to the app); Rust plugins.rs:48 (a panicking
// plugin must not take the app down). Owner unified (M2 wave 2).

namespace Barbican.Core.Plugins.Native;

/// <summary>A plugin failed to load, threw, or ran past its budget; the app carried on.</summary>
public sealed class PluginFailedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="pluginId">The plugin's id, or its folder name when the manifest could not be read.</param>
    /// <param name="where">The hook or step ("load", "initialize", "email.details", "onAccountRemove").</param>
    /// <param name="error">What went wrong.</param>
    public PluginFailedEventArgs(string pluginId, string where, Exception error)
    {
        PluginId = pluginId;
        Where = where;
        Error = error;
    }

    /// <summary>The plugin's id, or its folder name.</summary>
    public string PluginId { get; }

    /// <summary>The hook or step.</summary>
    public string Where { get; }

    /// <summary>What went wrong.</summary>
    public Exception Error { get; }
}
