// Mirrors: Rust ai.rs, agent.rs (rust-final); spec/behaviours/assistant.md B-AI-001..011; Microsoft.Extensions.AI is the option for the provider layer (PORT-PLAN M6, D-M6-A05). Bulwark has no assistant. Contract from the M6 plan section 4.4; owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.Plugin.Assistant;

/// <summary>A tool the model may call (B-AI-008); Writes tools ask first in Ask mode.</summary>
public sealed record AssistantToolSpec(string Name, string Description, JsonObject Schema, bool Writes);
