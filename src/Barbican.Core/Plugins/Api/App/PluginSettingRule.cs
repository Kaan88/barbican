// Mirrors: Bulwark lib/settings-schema.ts:60-82 SettingSchemaEntry (kind, `only` direction, the values left out of a choice).
namespace Barbican.Core.Plugins.Api.App;

/// <summary>What one allow-listed setting accepts.</summary>
/// <param name="Kind">How the value is checked.</param>
/// <param name="PrefsKey">The key the app stores it under, when it is not the add-on's name for it.</param>
/// <param name="Only">For a boolean: the only value an add-on may set (a delete safety net may only be turned on).</param>
/// <param name="Left">For a choice: row values an add-on may not choose (Bulwark's table leaves out "delete permanently", "trash after export" and the 60 s undo-send).</param>
/// <param name="Values">For a choice whose catalog row has no options of its own: the values.</param>
/// <param name="RowId">For a setting that is a keyless catalog row (the theme, the calendar's opening view).</param>
internal sealed record PluginSettingRule(PluginSettingKind Kind, string? PrefsKey = null, bool? Only = null, string[]? Left = null, string[]? Values = null, string? RowId = null);
