// Mirrors: Rust jmap.rs reads of serde_json::Value (`v["x"].as_str()`, `.as_u64()`, `.as_array()`): the same lenient reads of a
// JSON tree, where a missing or mistyped member is "absent", never an exception (RFC 8620 §3.4 responses are server data). Owner jmap.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Json;

/// <summary>Lenient typed reads of a <see cref="JsonNode"/> tree.</summary>
public static class JsonNodeExtensions
{
    /// <summary>The string value of this node, or null.</summary>
    public static string? AsString(this JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    /// <summary>The string member <paramref name="key"/>, or null.</summary>
    public static string? Str(this JsonNode? node, string key) => node is JsonObject o ? o[key].AsString() : null;

    /// <summary>The non-negative integer member <paramref name="key"/>, or 0.</summary>
    public static ulong Number(this JsonNode? node, string key) => node is JsonObject o ? o[key].AsNumber() ?? 0 : 0;

    /// <summary>The non-negative integer value of this node, or null.</summary>
    public static ulong? AsNumber(this JsonNode? node)
    {
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        // a parsed value and one built from an int, long or double all print as their JSON number
        var text = v.ToJsonString();
        if (ulong.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            && x >= 0 && x <= ulong.MaxValue && Math.Floor(x) == x ? (ulong)x : null;
    }

    /// <summary>The integer member <paramref name="key"/> clamped to int, or null.</summary>
    public static int? Count(this JsonNode? node, string key) =>
        node is JsonObject o && o[key].AsNumber() is { } n ? (int)Math.Min(n, int.MaxValue) : null;

    /// <summary>True only when member <paramref name="key"/> is the JSON literal true.</summary>
    public static bool True(this JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    /// <summary>The member <paramref name="key"/> as an array, or null.</summary>
    public static JsonArray? Arr(this JsonNode? node, string key) => node is JsonObject o ? o[key] as JsonArray : null;

    /// <summary>The member <paramref name="key"/> as an object, or null.</summary>
    public static JsonObject? Obj(this JsonNode? node, string key) => node is JsonObject o ? o[key] as JsonObject : null;

    /// <summary>The string items of the array member <paramref name="key"/> (non-strings skipped); empty when absent.</summary>
    public static IReadOnlyList<string> Strings(this JsonNode? node, string key) =>
        node.Arr(key) is { } a ? [.. a.Select(x => x.AsString()).OfType<string>()] : [];
}
