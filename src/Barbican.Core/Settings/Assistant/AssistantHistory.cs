// Mirrors: Rust agent.rs:556-573 to_json, from_json (a chat survives a restart; one file per account in the profile, B-AI-011). Contract from the M6 plan section 4.4; owner set-data.
// The shape is the message shape (role, text, toolCalls, toolCallId), not the Rust {u, a, r} shape: Rust's file is never read (D-M6-A08).

using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The conversation as JSON.</summary>
public static class AssistantHistory
{
    /// <summary>The messages as a JSON array (role, text, toolCalls, toolCallId; absent fields omitted).</summary>
    public static JsonArray ToJson(IReadOnlyList<AssistantMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var array = new JsonArray();
        foreach (var m in messages)
        {
            var o = new JsonObject { ["role"] = m.Role, ["text"] = m.Text };
            if (m.ToolCalls is { } calls)
            {
                o["toolCalls"] = calls.DeepClone();
            }

            if (m.ToolCallId is { } id)
            {
                o["toolCallId"] = id;
            }

            AssistantJson.Push(array, o);
        }

        return array;
    }

    /// <summary>The messages back; an entry that is not an object or lacks a role is skipped, never an exception.</summary>
    public static IReadOnlyList<AssistantMessage> FromJson(JsonNode? json)
    {
        if (json is not JsonArray array)
        {
            return [];
        }

        var list = new List<AssistantMessage>();
        foreach (var node in array)
        {
            if (node is not JsonObject o || Str(o["role"]) is not { Length: > 0 } role)
            {
                continue;
            }

            list.Add(new AssistantMessage(role, Str(o["text"]) ?? string.Empty, o["toolCalls"] is JsonArray calls ? calls.DeepClone() : null, Str(o["toolCallId"])));
        }

        return list;
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
