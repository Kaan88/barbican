// Mirrors: Rust filters_x.rs (rust-final; the file:line of each member is on it); Bulwark lib/filters/retroactive.ts, components/settings/filter-settings.tsx, filter-rule-modal.tsx, lib/sieve/generator.ts, lib/sieve/period.ts; RFC 5228 (Sieve), RFC 8621 section 4.4 (Email/query filters), RFC 8620 section 5.3 (ifInState); Stalwart crates/jmap/src/sieve/. B-SIEVE-013..023, B-SIEVE-027, B-SIEVE-028. Contract from the M6 plan section 4.6; owner srv-data.

namespace Barbican.Core.Filters.Rules;

/// <summary>One step of the run-on-existing-mail plan (Rust filters_x.rs:404 plan): kind keyword, move or copy, the email ids, the keyword or mailbox id.</summary>
public sealed record FilterPlanStep(string Kind, IReadOnlyList<string> Ids, string Arg);
