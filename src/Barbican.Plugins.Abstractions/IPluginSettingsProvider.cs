// Mirrors: nothing in Bulwark (native add-on host services, API 1 additive; decision D-M6b-08). Optional: a plugin class implements it to get a settings page.

namespace Barbican.Plugins;

/// <summary>Implemented by a plugin that has settings: the rows the host draws for it.</summary>
public interface IPluginSettingsProvider
{
    /// <summary>The page title ("AI assistant").</summary>
    string SettingsTitle { get; }

    /// <summary>One sentence under the title.</summary>
    string SettingsIntro { get; }

    /// <summary>The rows, in order.</summary>
    IReadOnlyList<PluginSettingRow> SettingRows { get; }

    /// <summary>Called after the host stored a changed value of this account: more values to store with it (a provider change that swaps the default address), or none.</summary>
    IReadOnlyDictionary<string, string?> AfterSettingChanged(string accountKey, string key);
}
