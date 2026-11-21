// Mirrors: nothing in Bulwark (native add-on host services, decision D-M6b-08). The page of a running native add-on, from what its class implements.

using Barbican.Core.Plugins.Native;
using Barbican.Core.Plugins.Native.Host;
using Barbican.Plugins;

namespace Barbican.Presentation.Settings.Addons;

/// <summary>The default <see cref="IAddonPageSource"/> over the native plugin host.</summary>
public sealed class NativeAddonPageSource(NativePlugins plugins, IPluginHostServices services) : IAddonPageSource
{
    /// <inheritdoc/>
    public AddonPage? Find(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        var plugin = plugins.Find(pluginId);
        var settings = plugin as IPluginSettingsProvider;
        IPluginChat? chat = null;
        if (plugin is IPluginChatProvider provider)
        {
            try
            {
                chat = provider.Chat;
            }
            catch (InvalidOperationException)
            {
                // the add-on has not finished starting: no chat yet
            }
        }

        if (plugin is null || (settings is null && chat is null))
        {
            return null;
        }

        return new AddonPage(
            pluginId,
            settings?.SettingsTitle ?? plugin.Manifest.Name,
            settings?.SettingsIntro ?? plugin.Manifest.Description,
            settings?.SettingRows ?? [],
            services.SettingsOf(pluginId),
            services.SecretsOf(pluginId),
            (account, key) => settings?.AfterSettingChanged(account, key) ?? new Dictionary<string, string?>(StringComparer.Ordinal),
            chat);
    }
}
