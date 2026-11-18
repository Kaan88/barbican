// Mirrors: Rust ai.rs, agent.rs (rust-final); spec/behaviours/assistant.md B-AI-001..011; Microsoft.Extensions.AI is the option for the provider layer (PORT-PLAN M6, D-M6-A05). Bulwark has no assistant. Contract from the M6 plan section 4.4; owner set-data.

namespace Barbican.Core.Settings.Assistant;

/// <summary>How far the assistant may act (B-AI-006, B-AI-009).</summary>
public enum AssistantActionMode
{
    /// <summary>Read-only tools only.</summary>
    Off = 0,

    /// <summary>Every write tool asks first (the default); send_email always asks.</summary>
    Ask = 1,

    /// <summary>Write tools run without asking; send_email still asks (B-AI-009).</summary>
    Automatic = 2,
}
