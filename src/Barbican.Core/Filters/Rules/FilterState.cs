// Mirrors: Rust filters_x.rs (rust-final; the file:line of each member is on it); Bulwark lib/filters/retroactive.ts, components/settings/filter-settings.tsx, filter-rule-modal.tsx, lib/sieve/generator.ts, lib/sieve/period.ts; RFC 5228 (Sieve), RFC 8621 section 4.4 (Email/query filters), RFC 8620 section 5.3 (ifInState); Stalwart crates/jmap/src/sieve/. B-SIEVE-013..023, B-SIEVE-027, B-SIEVE-028. Contract from the M6 plan section 4.6; owner srv-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Filters.Rules;

/// <summary>The account's filters: the rules of the metadata block, the hand-written external part, whether the script is locked (hand-edited: no visual edit, B-SIEVE-007), the server's Sieve extensions and whether it advertises reject.</summary>
public sealed record FilterState(IReadOnlyList<JsonNode> Rules, string External, bool Locked, IReadOnlyList<string> Extensions, bool RejectAdvertised);
