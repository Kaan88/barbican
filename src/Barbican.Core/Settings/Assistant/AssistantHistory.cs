// Mirrors: Rust agent.rs:556-573 to_json, from_json (a chat survives a restart; one file per account in the profile, B-AI-011). Contract from the M6 plan section 4.4; owner set-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The conversation as JSON.</summary>
public static class AssistantHistory
{
    /// <summary>The messages as a JSON array (role, text, toolCalls, toolCallId; absent fields omitted).</summary>
    public static JsonArray ToJson(IReadOnlyList<AssistantMessage> messages) => throw new PortPendingException("AssistantHistory.ToJson");

    /// <summary>The messages back; an entry that is not an object or lacks a role is skipped, never an exception.</summary>
    public static IReadOnlyList<AssistantMessage> FromJson(JsonNode? json) => throw new PortPendingException("AssistantHistory.FromJson");
}
