// Mirrors: Bulwark lib/settings-schema.ts SettingSchemaEntry joined with its settings page row (label, page, values).
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Plugins.Api.App;

/// <summary>An allow-listed setting with the catalog row that shows it. <see cref="Name"/> is the add-on's key (calendarView is viewMode), <see cref="PrefsKey"/> the key the app stores it under.</summary>
internal sealed record PluginResolvedSetting(string Name, PluginSettingRule Rule, SettingRow Row, string PrefsKey);
