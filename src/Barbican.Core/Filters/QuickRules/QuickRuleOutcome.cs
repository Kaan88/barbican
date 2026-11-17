// Mirrors: Rust quick_rules.rs:169 Outcome; Bulwark applyQuickRule. Stub from stage F of M2; owner search.

using System.Text.Json.Nodes;

namespace Barbican.Core.Filters.QuickRules;

/// <summary>What saving a one-click rule did.</summary>
public abstract record QuickRuleOutcome
{
    /// <summary>A new rule, at the top.</summary>
    public sealed record Created(IReadOnlyList<JsonNode> Rules) : QuickRuleOutcome;

    /// <summary>An existing rule (its name) took the new values.</summary>
    public sealed record Merged(IReadOnlyList<JsonNode> Rules, string Name) : QuickRuleOutcome;

    /// <summary>An existing rule (its name) already covers it: nothing to write.</summary>
    public sealed record Covered(string Name) : QuickRuleOutcome;
}
