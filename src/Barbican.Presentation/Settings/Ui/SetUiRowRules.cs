// Mirrors: Bulwark settings-section.tsx (a section with no visible row is hidden), settings-app.tsx ("Show advanced settings", I-398), components/settings/settings-search anchors. Contract from the M6 plan section 4.10; owner set-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The pure rules of how rows are shown.</summary>
public static class SetUiRowRules
{
    // The two toggles of the schema that show the opposite of what the key stores ("Group messages by conversation" over disableThreading,
    // "Settings sync" over settingsSyncDisabled; settings-schema.json `invert`, a field the catalog record does not carry).
    private static readonly HashSet<string> InvertedKeys = new(["disableThreading", "settingsSyncDisabled"], StringComparer.Ordinal);

    // The keyless select rows the shell already reads under a pref (Rust settings.rs kept the key in code, not in the schema): "Light or dark" is "theme" (light, dark, system).
    private static readonly Dictionary<string, string> KeylessKeys = new(StringComparer.Ordinal) { ["S-keyless-025"] = "theme", ["S-keyless-049"] = "calendarViewMode" };

    private static readonly Dictionary<string, string> KeylessDefaults = new(StringComparer.Ordinal) { ["S-keyless-025"] = "system", ["S-keyless-049"] = "month" };

    private static readonly Dictionary<string, (string Field, object Default)> FileFields = new(StringComparer.Ordinal)
    {
        ["S-keyless-054"] = ("defaultViewMode", "list"), ["S-keyless-055"] = ("defaultSortKey", "name"),
        ["S-keyless-056"] = ("defaultSortDir", "asc"), ["S-keyless-057"] = ("showIcons", true),
        ["S-keyless-058"] = ("coloredIcons", true), ["S-keyless-059"] = ("showThumbnails", true),
        ["S-keyless-060"] = ("showHiddenFiles", false),
    };

    /// <summary>Field inside the shared Files settings object, if this is a Files control.</summary>
    public static string? FileField(string rowId) => FileFields.TryGetValue(rowId, out var field) ? field.Field : null;

    /// <summary>Read a Files control's value, using the same defaults as FilesSettings.</summary>
    public static JsonNode? FileValue(string rowId, JsonNode? stored)
    {
        if (!FileFields.TryGetValue(rowId, out var field))
        {
            return null;
        }

        return (stored as JsonObject)?[field.Field]?.DeepClone() ?? field.Default switch { string text => JsonValue.Create(text), bool flag => JsonValue.Create(flag), _ => null };
    }

    /// <summary>The pref key a row writes: its own, else the one the app reads for a known keyless row, else null (an info row or a handler).</summary>
    public static string? KeyOf(SettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Key ?? (FileField(row.Id) is not null ? "filesSettings" : KeylessKeys.TryGetValue(row.Id, out var k) ? k : null);
    }

    /// <summary>The rows that show: advanced rows only with <paramref name="showAdvanced"/>, and a section row only when at least one row after it (up to the next section) shows. Order is kept.</summary>
    public static IReadOnlyList<SettingRow> Visible(IReadOnlyList<SettingRow> rows, bool showAdvanced)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = new List<SettingRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Advanced && !showAdvanced)
            {
                continue;
            }

            if (row.Kind != SettingRowKind.Section)
            {
                result.Add(row);
                continue;
            }

            var any = false;
            for (var j = i + 1; j < rows.Count && rows[j].Kind != SettingRowKind.Section; j++)
            {
                if (showAdvanced || !rows[j].Advanced)
                {
                    any = true;
                    break;
                }
            }

            if (any)
            {
                result.Add(row);
            }
        }

        return result;
    }

    /// <summary>The stored value, else the row's default.</summary>
    public static JsonNode? EffectiveValue(SettingRow row, JsonNode? stored)
    {
        ArgumentNullException.ThrowIfNull(row);
        return stored ?? row.Default?.DeepClone() ?? (KeylessDefaults.TryGetValue(row.Id, out var d) ? JsonValue.Create(d) : null);
    }

    /// <summary>The text a select row shows for a value: the label of the matching option, else the value itself, else the label of the first option for an unset value.</summary>
    public static string SelectText(SettingRow row, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (value is null)
        {
            return row.Options.Count > 0 ? row.Options[0].Label : string.Empty;
        }

        var text = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value.ToJsonString();
        foreach (var option in row.Options)
        {
            if (string.Equals(option.Value, text, StringComparison.Ordinal))
            {
                return option.Label;
            }
        }

        return text;
    }

    /// <summary>The toggle shows the opposite of its stored value.</summary>
    public static bool IsInverted(SettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Key is { } k && InvertedKeys.Contains(k);
    }

    /// <summary>A toggle's state as the user sees it (the stored boolean, flipped for an inverted row; an unset value reads as off).</summary>
    public static bool ToggleOn(SettingRow row, JsonNode? value)
    {
        var on = value is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : v.TryGetValue<string>(out var s) && bool.TryParse(s, out var p) && p);
        return IsInverted(row) ? !on : on;
    }

    /// <summary>The boolean to store when the user sets a toggle to <paramref name="shownOn"/>.</summary>
    public static bool ToStored(SettingRow row, bool shownOn) => IsInverted(row) ? !shownOn : shownOn;
}
