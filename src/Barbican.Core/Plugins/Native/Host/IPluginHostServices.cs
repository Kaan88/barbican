// Mirrors: nothing in Bulwark (native add-on host services, decision D-M6b-08). The native host asks this for what to put in a plugin's PluginContext; the settings page asks it for the same settings and secrets the plugin uses.

using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native.Host;

/// <summary>What the app offers a native add-on beyond hooks.</summary>
public interface IPluginHostServices
{
    /// <summary>The context with the host services of <paramref name="pluginId"/> filled in (its settings, secrets, HTTP client, tools, dialogs).</summary>
    PluginContext Apply(PluginContext context, string pluginId);

    /// <summary>The settings of the add-on (the same instance the add-on gets).</summary>
    IPluginSettings SettingsOf(string pluginId);

    /// <summary>The secrets of the add-on (what the add-on's secret rows write).</summary>
    IPluginSecrets SecretsOf(string pluginId);
}
