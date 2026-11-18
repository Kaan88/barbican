// Mirrors: Rust ai.rs, agent.rs (rust-final); spec/behaviours/assistant.md B-AI-001..011; Microsoft.Extensions.AI is the option for the provider layer (PORT-PLAN M6, D-M6-A05). Bulwark has no assistant. Contract from the M6 plan section 4.4; owner set-data.

namespace Barbican.Core.Settings.Assistant;

/// <summary>The two provider kinds (B-AI-001).</summary>
public enum AssistantProviderKind
{
    /// <summary>POST {base}/chat/completions with a bearer key.</summary>
    OpenAiCompatible = 0,

    /// <summary>POST {base}/messages with x-api-key and anthropic-version 2023-06-01.</summary>
    Anthropic = 1,
}
