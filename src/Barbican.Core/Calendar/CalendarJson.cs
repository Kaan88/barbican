// Mirrors: Rust serde_json reads used all over calendar.rs, recurrence.rs and reminders.rs (`v["x"].as_str()`, `.as_i64()`, `== true`): a missing or mistyped member is "absent", never an exception. Owner cal-data.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar;

/// <summary>Lenient typed reads of JSCalendar objects (server data).</summary>
internal static class CalendarJson
{
    public static string? Str(this JsonNode? node, string key) => node is JsonObject o && o[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    public static bool IsTrue(this JsonNode? node, string key) => node is JsonObject o && o[key] is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    public static bool IsString(this JsonNode? node, string key) => node.Str(key) is not null;

    public static JsonNode? Child(this JsonNode? node, string key) => node is JsonObject o ? o[key] : null;

    public static JsonObject? Obj(this JsonNode? node, string key) => node is JsonObject o ? o[key] as JsonObject : null;

    public static JsonArray? Arr(this JsonNode? node, string key) => node is JsonObject o ? o[key] as JsonArray : null;

    /// <summary>The integer value of a number node (whole numbers and doubles with no fraction), or null.</summary>
    public static long? AsLong(this JsonNode? node)
    {
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        var text = v.ToJsonString();
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && Math.Abs(x) < 9e18 && Math.Floor(x) == x ? (long)x : null;
    }

    public static long? Long(this JsonNode? node, string key) => node.Child(key).AsLong();
}
