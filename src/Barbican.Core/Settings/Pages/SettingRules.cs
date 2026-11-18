// Mirrors: Bulwark stores/settings-store.ts (typed setters), components/settings/settings-section.tsx (SettingItem kinds); B-SET-001..005. Contract from the M6 plan section 4.2; owner set-data.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Pages;

/// <summary>The pure rules a written value follows before it reaches prefs.json.</summary>
public static class SettingRules
{
    /// <summary>Checks a value for a row: a toggle takes a boolean (the strings "true" and "false" are coerced), a select one of its option values, a text a trimmed string; null resets to the default. Info, button and section rows refuse any value.</summary>
    public static SettingCheck Coerce(SettingRow row, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Kind is SettingRowKind.Info or SettingRowKind.Button or SettingRowKind.Section)
        {
            return Bad("This row holds no value.");
        }

        if (value is null)
        {
            return new SettingCheck(true, null, null);
        }

        return row.Kind switch
        {
            SettingRowKind.Toggle => Toggle(value),
            SettingRowKind.Select => Select(row, value),
            _ => Text(value),
        };
    }

    private static SettingCheck Toggle(JsonNode value)
    {
        if (value is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b))
            {
                return Good(JsonValue.Create(b));
            }

            if (v.TryGetValue<string>(out var s) && bool.TryParse(s.Trim(), out var parsed))
            {
                return Good(JsonValue.Create(parsed));
            }
        }

        return Bad("This setting is on or off.");
    }

    private static SettingCheck Select(SettingRow row, JsonNode value)
    {
        var text = AsText(value);
        if (text is null)
        {
            return Bad("Choose one of the listed values.");
        }

        if (row.Options.Count > 0 && !row.Options.Any(o => string.Equals(o.Value, text, StringComparison.Ordinal)))
        {
            return Bad("That is not one of the choices.");
        }

        return Good(JsonValue.Create(text));
    }

    private static SettingCheck Text(JsonNode value) =>
        value is JsonValue v && v.TryGetValue<string>(out var s) ? Good(JsonValue.Create(s.Trim())) : Bad("This setting is text.");

    private static string? AsText(JsonNode value)
    {
        if (value is not JsonValue v)
        {
            return null;
        }

        if (v.TryGetValue<string>(out var s))
        {
            return s;
        }

        if (v.TryGetValue<long>(out var l))
        {
            return l.ToString(CultureInfo.InvariantCulture);
        }

        return v.TryGetValue<bool>(out var b) ? (b ? "true" : "false") : null;
    }

    private static SettingCheck Good(JsonNode? value) => new(true, value, null);

    private static SettingCheck Bad(string error) => new(false, null, error);
}
