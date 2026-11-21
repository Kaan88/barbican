// Mirrors: Rust ai.rs (JSON message construction).
using System.Text.Json.Nodes;

namespace Barbican.Plugin.Assistant;

internal static class AssistantJson
{
    internal static void Push(JsonArray array, JsonNode node) => array.Add(node);
}
