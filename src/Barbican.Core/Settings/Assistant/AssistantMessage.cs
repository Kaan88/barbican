// Mirrors: Rust ai.rs, agent.rs (rust-final); spec/behaviours/assistant.md B-AI-001..011; Microsoft.Extensions.AI is the option for the provider layer (PORT-PLAN M6, D-M6-A05). Bulwark has no assistant. Contract from the M6 plan section 4.4; owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Assistant;

/// <summary>One turn of a conversation: role user, assistant or tool; ToolCalls (on an assistant turn that asked for tools) is a JSON array of {id, name, arguments}; ToolCallId on a tool turn.</summary>
public sealed record AssistantMessage(string Role, string Text, JsonNode? ToolCalls, string? ToolCallId);
