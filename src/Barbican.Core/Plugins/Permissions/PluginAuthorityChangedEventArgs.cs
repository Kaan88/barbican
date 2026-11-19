// Mirrors: Bulwark permission/update transitions; generations revoke host-owned resources across API and UI lifetimes.
namespace Barbican.Core.Plugins.Permissions;
public sealed class PluginAuthorityChangedEventArgs(string pluginId, long generation) : EventArgs
{
    public string PluginId { get; } = pluginId;
    public long Generation { get; } = generation;
}
