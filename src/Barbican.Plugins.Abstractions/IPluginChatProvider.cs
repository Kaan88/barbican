// Mirrors: the add-on chat panel contract (decision D-M6b-08). Optional: a plugin class implements it to get a chat panel.

namespace Barbican.Plugins;

/// <summary>Implemented by a plugin that talks with the user: the host shows its chat in the add-on's panel.</summary>
public interface IPluginChatProvider
{
    /// <summary>The conversation (valid after <see cref="IBarbicanPlugin.InitializeAsync"/>).</summary>
    IPluginChat Chat { get; }
}
