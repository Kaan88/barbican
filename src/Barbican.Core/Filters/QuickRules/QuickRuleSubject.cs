// Mirrors: Rust quick_rules.rs:29 Subject (what the presets are made from); Bulwark lib/quick-rule.ts. Stub from stage F of M2; owner search.

namespace Barbican.Core.Filters.QuickRules;

/// <summary>The sender, domain and list of the message a quick rule is made from.</summary>
/// <param name="Senders">Lower-cased sender addresses, the user's own left out.</param>
/// <param name="Domain">The sender domain, or null.</param>
/// <param name="ListId">The List-Id, or null.</param>
public sealed record QuickRuleSubject(IReadOnlyList<string> Senders, string? Domain, string? ListId);
