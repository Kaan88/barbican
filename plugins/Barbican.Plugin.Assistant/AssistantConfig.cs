// Mirrors: Rust ai.rs, agent.rs (rust-final); spec/behaviours/assistant.md B-AI-001..011; Microsoft.Extensions.AI is the option for the provider layer (PORT-PLAN M6, D-M6-A05). Bulwark has no assistant. Contract from the M6 plan section 4.4; owner set-data.

namespace Barbican.Plugin.Assistant;

/// <summary>The assistant settings of the desktop settings.json (B-AI-006). The API key is not here: it lives in the OS credential store under `ai-api-key` (B-AI-002).</summary>
public sealed record AssistantConfig(AssistantProviderKind Provider, string Base, string Model, string Language, AssistantActionMode Actions, bool Memory, string Instructions, IReadOnlyList<string> Notes);
