// Mirrors: Rust quick_rules.rs:218 plan (Ok(Ok((rules, toast))) or Ok(Err(toast)) for nothing to save); Bulwark applyQuickRule. Stub from stage F of M2; owner search.

using System.Text.Json.Nodes;

namespace Barbican.Core.Filters.QuickRules;

/// <summary>What saving a candidate rule into a parsed script comes to.</summary>
/// <param name="Rules">The rules to write, or null when nothing needs saving.</param>
/// <param name="Toast">The message to show ("Rule created", "Added to rule", "Already covered by").</param>
public sealed record QuickRulePlan(IReadOnlyList<JsonNode>? Rules, string Toast)
{
    /// <summary>True when there is something to write.</summary>
    public bool NeedsSave => Rules is not null;
}
