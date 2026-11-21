// Mirrors: nothing in Bulwark (native add-on host services, API 1 additive; decision D-M6b-08). The host draws these with its generic settings rows.

namespace Barbican.Plugins;

/// <summary>One setting an add-on declares; the host draws a row for it on the add-on's settings page and stores the value per account.</summary>
/// <param name="Key">The stable key of the value ("model"). For <see cref="PluginSettingKind.Secret"/> it names the secret.</param>
/// <param name="Label">The row title.</param>
/// <param name="Description">The sentence under the title.</param>
/// <param name="Kind">The control.</param>
/// <param name="Choices">The options of a choice row (null otherwise).</param>
/// <param name="Placeholder">The hint of an empty text row.</param>
/// <param name="Default">The value shown while nothing is stored ("true" / "false" for a toggle, an option value for a choice).</param>
public sealed record PluginSettingRow(
    string Key,
    string Label,
    string Description,
    PluginSettingKind Kind,
    IReadOnlyList<PluginChoice>? Choices = null,
    string Placeholder = "",
    string Default = "");
