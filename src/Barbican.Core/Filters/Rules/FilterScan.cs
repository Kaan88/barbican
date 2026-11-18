// Mirrors: Rust filters_x.rs (rust-final; the file:line of each member is on it); Bulwark lib/filters/retroactive.ts, components/settings/filter-settings.tsx, filter-rule-modal.tsx, lib/sieve/generator.ts, lib/sieve/period.ts; RFC 5228 (Sieve), RFC 8621 section 4.4 (Email/query filters), RFC 8620 section 5.3 (ifInState); Stalwart crates/jmap/src/sieve/. B-SIEVE-013..023, B-SIEVE-027, B-SIEVE-028. Contract from the M6 plan section 4.6; owner srv-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Filters.Rules;

/// <summary>What "Apply to existing mail" would do: the plan, how many messages, why it cannot (Unsupported), the generation that makes a stale scan droppable, and the Email state the apply is guarded with.</summary>
public sealed record FilterScan(JsonNode Rule, IReadOnlyList<FilterPlanStep> Steps, int Total, string? Unsupported, long Generation, string State);
