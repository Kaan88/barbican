// Lenient reads and small builders over JSContact objects (RFC 9553); cards are JsonNode all the way (D-M5-A02: unknown properties must survive a save). Mirrors the `v["x"].as_str()` reads of Rust contacts_x.rs / vcard.rs. Owner con-data.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>Reads of a card tree that never throw on a mistyped member, and builders that never share a node between two parents.</summary>
internal static class ContactJson
{
    /// <summary>The member <paramref name="key"/> of an object, or null (also for a node that is not an object).</summary>
    public static JsonNode? Get(JsonNode? node, string key) => node is JsonObject o ? o[key] : null;

    /// <summary>The entries of a JSContact map (emails, phones, ...): key and value, in the order of the card.</summary>
    public static List<(string Key, JsonNode Value)> Entries(JsonNode? card, string map)
    {
        var list = new List<(string, JsonNode)>();
        if (Get(card, map) is JsonObject o)
        {
            foreach (var (k, v) in o)
            {
                if (v is not null)
                {
                    list.Add((k, v));
                }
            }
        }

        return list;
    }

    /// <summary>The values of a JSContact map.</summary>
    public static List<JsonNode> Values(JsonNode? card, string map) => [.. Entries(card, map).Select(e => e.Value)];

    public static bool IsTrue(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    /// <summary>An integer member of any JSON number type (an int built in memory or a long parsed from text), or null.</summary>
    public static long? Int(JsonNode? node)
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

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && Math.Floor(d) == d && Math.Abs(d) < 1e15 ? (long)d : null;
    }

    /// <summary>A JSON number: an int when it fits (so a cast to int of a built card works), else a long.</summary>
    public static JsonNode Num(long value) => value is >= int.MinValue and <= int.MaxValue ? JsonValue.Create((int)value) : JsonValue.Create(value);

    /// <summary>Adds a node to an array without the generic overload (the generic Add of JsonArray is not trim-safe and the build refuses it).</summary>
    public static void Append(this JsonArray array, JsonNode item) => array.Add(item);

    public static JsonNode Text(string value) => JsonValue.Create(value);

    public static JsonNode Yes() => JsonValue.Create(true);

    /// <summary>A copy of an object without its null members (the Rust <c>clean</c>).</summary>
    public static JsonObject Clean(JsonObject o)
    {
        var copy = new JsonObject();
        foreach (var (k, v) in o)
        {
            if (v is not null)
            {
                copy[k] = v.DeepClone();
            }
        }

        return copy;
    }

    /// <summary>The first key of a flag map (relation, contexts, features) whose value is true.</summary>
    public static string? FirstTrueKey(JsonNode? flags)
    {
        if (flags is JsonObject o)
        {
            foreach (var (k, v) in o)
            {
                if (IsTrue(v))
                {
                    return k;
                }
            }
        }

        return null;
    }

    /// <summary>A lower-case key made from a string for comparisons: no culture rules apply to JSContact keys.</summary>
    public static string Lower(string s) => s.ToLowerInvariant();

    /// <summary>The first key "prefix0", "prefix1", ... that the map does not have.</summary>
    public static string FreeKey(JsonObject map, string prefix)
    {
        for (var i = 0; ; i++)
        {
            var k = prefix + i.ToString(CultureInfo.InvariantCulture);
            if (!map.ContainsKey(k))
            {
                return k;
            }
        }
    }

    /// <summary>A card's own non-empty string at a path, as text; empty when absent.</summary>
    public static string StrOrEmpty(JsonNode? node, string key) => node.Str(key) ?? string.Empty;
}
