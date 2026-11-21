// Mirrors: nothing in Bulwark (native add-on host services, decision D-M6b-08). What the host draws for one add-on on its Settings page: the rows it declared and its chat panel.

using Barbican.Plugins;

namespace Barbican.Presentation.Settings.Addons;

/// <summary>One add-on's settings page: title, rows, chat.</summary>
/// <param name="PluginId">The add-on's id.</param>
/// <param name="Title">The page title.</param>
/// <param name="Intro">The sentence under the title.</param>
/// <param name="Rows">The declared rows, in order.</param>
/// <param name="Settings">Where the row values live (per account).</param>
/// <param name="Secrets">Where the secret rows write.</param>
/// <param name="AfterChange">Extra values to store after a value changed (see <see cref="IPluginSettingsProvider.AfterSettingChanged"/>).</param>
/// <param name="Chat">The add-on's chat, or null when it has none.</param>
public sealed record AddonPage(
    string PluginId,
    string Title,
    string Intro,
    IReadOnlyList<PluginSettingRow> Rows,
    IPluginSettings Settings,
    IPluginSecrets Secrets,
    Func<string, string, IReadOnlyDictionary<string, string?>> AfterChange,
    IPluginChat? Chat);
