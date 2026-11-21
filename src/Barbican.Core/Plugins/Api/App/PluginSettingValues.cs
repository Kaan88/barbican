// Mirrors: Bulwark lib/settings-schema.ts:468-532 isAllowedSettingValue and settingValueLabelKey (a boolean takes a boolean, in the one direction a safety net allows; a choice takes exactly one of its values of its own JSON type, so the
// string "25" is not the number 25; a time zone is "auto" or one this machine knows) and assistant/c.settings.ts:42-54,83-130 (current value, value labels). Values are read from and written to the same store the settings pages use.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Plugins.Api.App;

internal static class PluginSettingValues
{
    private static readonly Regex ZoneName = new("^[A-Za-z][A-Za-z0-9_+\\-/]*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    /// <summary>The stored value, else the row's default; a switch nobody set reads as off.</summary>
    internal static JsonNode? Effective(ISettingValues values, SettingRow row, string prefsKey)
    {
        var stored = values.Stored(prefsKey);
        if (stored is not null) return stored;
        return row.Default?.DeepClone() ?? KeylessDefault(row.Id) ?? (row.Kind == SettingRowKind.Toggle ? JsonValue.Create(false) : null);
    }

    // the two keyless rows the app reads with a default of their own (SetUiRowRules.KeylessDefaults): the theme follows the system, the calendar opens on the month
    private static JsonValue? KeylessDefault(string rowId) => rowId switch
    {
        "S-keyless-025" => JsonValue.Create("system"),
        "S-keyless-049" => JsonValue.Create("month"),
        _ => null,
    };

    /// <summary>A plugin value as a JSON primitive (string, boolean or finite number); anything else is no value a setting takes.</summary>
    internal static bool TryRead(PluginValue? value, out JsonNode? json)
    {
        json = null;
        switch (value?.Kind)
        {
            case PluginValueKind.String:
                json = JsonValue.Create(value.StringValue);
                return true;
            case PluginValueKind.Boolean:
                json = JsonValue.Create(value.BooleanValue);
                return true;
            case PluginValueKind.Number when double.TryParse(value.NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number):
                json = JsonValue.Create(number);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether <paramref name="value"/> is one the setting may take now; <paramref name="canonical"/> is what is written.</summary>
    internal static bool Allows(PluginResolvedSetting setting, JsonNode? value, out JsonNode? canonical)
    {
        canonical = null;
        if (value is not JsonValue json) return false;
        switch (setting.Rule.Kind)
        {
            case PluginSettingKind.Boolean:
                if (json.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) return false;
                var flag = json.GetValueKind() == JsonValueKind.True;
                if (setting.Rule.Only is { } only && flag != only) return false;
                canonical = JsonValue.Create(flag);
                return true;
            case PluginSettingKind.TimeZone:
                if (!json.TryGetValue<string>(out var zone)) return false;
                if (!string.Equals(zone, "auto", StringComparison.Ordinal) && !IsKnownZone(zone)) return false;
                canonical = JsonValue.Create(zone);
                return true;
            default:
                foreach (var (option, _) in Choices(setting))
                {
                    if (Same(option, value))
                    {
                        canonical = option.DeepClone();
                        return true;
                    }
                }

                return false;
        }
    }

    /// <summary>The values a choice may take, each with its label (the row's own): numbers where the row's default is a number.</summary>
    internal static IReadOnlyList<(JsonNode Value, string Label)> Choices(PluginResolvedSetting setting)
    {
        if (setting.Rule.Kind != PluginSettingKind.Choice) return [];
        if (setting.Rule.Values is { } own) return [.. own.Select(v => ((JsonNode)JsonValue.Create(v)!, string.Equals(v, "all", StringComparison.Ordinal) ? "On" : "Off"))];
        var numeric = setting.Row.Default?.GetValueKind() == JsonValueKind.Number;
        var left = setting.Rule.Left ?? [];
        return
        [
            .. setting.Row.Options.Where(o => !left.Contains(o.Value, StringComparer.Ordinal))
                .Select(o => (numeric && double.TryParse(o.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (JsonNode)JsonValue.Create(n)! : JsonValue.Create(o.Value)!, o.Label)),
        ];
    }

    /// <summary>The value in the user's words: On or Off (turned round for a switch that says the opposite), the row's own label of a choice, else the value as text.</summary>
    internal static string Label(PluginResolvedSetting setting, JsonNode? value)
    {
        if (value is null) return "";
        if (setting.Rule.Kind == PluginSettingKind.Boolean && value is JsonValue b && b.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            var on = b.GetValueKind() == JsonValueKind.True;
            return (Inverted(setting) ? !on : on) ? "On" : "Off";
        }

        if (setting.Rule.Kind == PluginSettingKind.TimeZone) return value is JsonValue z && z.TryGetValue<string>(out var zone) && string.Equals(zone, "auto", StringComparison.Ordinal) ? "Automatic" : Text(value);
        foreach (var (option, label) in setting.Rule.Kind == PluginSettingKind.Choice ? AllOptions(setting) : [])
        {
            if (Same(option, value)) return label;
        }

        return Text(value);
    }

    /// <summary>Whether two primitives are the same value (the number 25 stored as an int and as a double are).</summary>
    internal static bool Same(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is JsonValue x && b is JsonValue y && x.GetValueKind() == JsonValueKind.Number && y.GetValueKind() == JsonValueKind.Number)
        {
            return double.Parse(x.ToJsonString(), CultureInfo.InvariantCulture) == double.Parse(y.ToJsonString(), CultureInfo.InvariantCulture);
        }

        return string.Equals(a.ToJsonString(), b.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>A stored primitive as a plugin value; null for anything that is not a string, boolean or finite number.</summary>
    internal static int? Emit(PluginValueBuilder b, JsonNode? value)
    {
        if (value is not JsonValue json) return null;
        switch (json.GetValueKind())
        {
            case JsonValueKind.String:
                return b.String(json.GetValue<string>());
            case JsonValueKind.True:
            case JsonValueKind.False:
                return b.Boolean(json.GetValueKind() == JsonValueKind.True);
            case JsonValueKind.Number:
                var n = double.Parse(json.ToJsonString(), CultureInfo.InvariantCulture);
                return double.IsFinite(n) ? b.Number(n) : null;
            default:
                return null;
        }
    }

    private static bool Inverted(PluginResolvedSetting setting) => setting.Row.Inverted || string.Equals(setting.Name, "disableThreading", StringComparison.Ordinal);

    // every value of the row, including the ones an add-on may not choose: the label of a value the user has
    private static IEnumerable<(JsonNode Value, string Label)> AllOptions(PluginResolvedSetting setting)
    {
        if (setting.Rule.Values is not null) return Choices(setting);
        var numeric = setting.Row.Default?.GetValueKind() == JsonValueKind.Number;
        return setting.Row.Options.Select(o => (numeric && double.TryParse(o.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (JsonNode)JsonValue.Create(n)! : JsonValue.Create(o.Value)!, o.Label));
    }

    private static string Text(JsonNode value) => value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value.ToJsonString();

    // an IANA id as Intl spells it ("Europe/Berlin", "UTC"); this machine's database decides
    private static bool IsKnownZone(string zone) =>
        zone.Length is > 0 and <= 64 && ZoneName.IsMatch(zone) && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _);
}
