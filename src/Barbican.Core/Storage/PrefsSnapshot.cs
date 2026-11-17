// Mirrors: Rust settings.rs Settings::{str :195, bool :191, int :200, with_prefs :117} (read side); Bulwark stores/settings-store.ts keys.
// Contract stub from F2, owner accounts. The mailcore stream implemented the three typed reads and FromJson because A11y (T-001..T-003)
// cannot pass without them; accounts keeps ownership and may replace the bodies (same behaviour) when it lands.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Storage;

/// <summary>A read-only view of prefs.json with typed reads and defaults.</summary>
public sealed class PrefsSnapshot
{
    private readonly JsonObject _values;

    private PrefsSnapshot(JsonObject values) => _values = values;

    /// <summary>The raw values (do not mutate).</summary>
    public JsonObject Values => _values;

    /// <summary>A snapshot over the given values (Rust <c>Settings::with_prefs</c>).</summary>
    public static PrefsSnapshot FromJson(JsonObject values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new PrefsSnapshot(values);
    }

    /// <summary>A string pref, or the default when absent or not a string.</summary>
    public string Str(string key, string fallback) =>
        _values[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;

    /// <summary>A boolean pref (also accepts "true"/"false" strings), or the default.</summary>
    public bool Bool(string key, bool fallback)
    {
        if (_values[key] is not JsonValue v)
        {
            return fallback;
        }

        if (v.TryGetValue<bool>(out var b))
        {
            return b;
        }

        return v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed) ? parsed : fallback;
    }

    /// <summary>A numeric pref (a number or a numeric string), or the default.</summary>
    public double Number(string key, double fallback)
    {
        if (_values[key] is not JsonValue v)
        {
            return fallback;
        }

        if (v.TryGetValue<double>(out var d))
        {
            return d;
        }

        if (v.TryGetValue<long>(out var l))
        {
            return l;
        }

        return v.TryGetValue<string>(out var s) && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }
}
