// Email/query filters of the fake server: RFC 8621 §4.4.1 (inMailbox, inMailboxOtherThan, hasKeyword, notKeyword, text, from,
// subject) and RFC 8620 §5.5 (FilterOperator AND / OR / NOT). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>Decides whether a message matches a filter.</summary>
public static class FilterEvaluator
{
    /// <summary>True when <paramref name="email"/> matches (no filter matches everything).</summary>
    public static bool Matches(JsonObject email, JsonNode? filter)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (filter is not JsonObject f || f.Count == 0)
        {
            return true;
        }

        if (f["operator"] is JsonValue op)
        {
            var parts = (f["conditions"] as JsonArray ?? []).Select(c => Matches(email, c)).ToList();
            return (string?)op switch
            {
                "AND" => parts.TrueForAll(x => x),
                "OR" => parts.Exists(x => x),
                "NOT" => !parts.Exists(x => x),
                _ => false,
            };
        }

        return f.All(kv => Condition(email, kv.Key, kv.Value));
    }

    private static bool Condition(JsonObject e, string key, JsonNode? value)
    {
        var boxes = e["mailboxIds"] as JsonObject ?? [];
        var keywords = e["keywords"] as JsonObject ?? [];
        return key switch
        {
            "inMailbox" => boxes.ContainsKey((string)value!),
            "inMailboxOtherThan" => boxes.Any(b => !(value as JsonArray ?? []).Any(x => string.Equals((string?)x, b.Key, StringComparison.Ordinal))),
            "hasKeyword" => keywords.ContainsKey((string)value!),
            "notKeyword" => !keywords.ContainsKey((string)value!),
            "text" => Text(e).Contains((string)value!, StringComparison.OrdinalIgnoreCase),
            "subject" => ((string?)e["subject"] ?? "").Contains((string)value!, StringComparison.OrdinalIgnoreCase),
            "from" => (e["from"]?.ToJsonString() ?? "").Contains((string)value!, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string Text(JsonObject e) => string.Join('\n', (string?)e["subject"], (string?)e["preview"], e["from"]?.ToJsonString(), e["to"]?.ToJsonString());
}
