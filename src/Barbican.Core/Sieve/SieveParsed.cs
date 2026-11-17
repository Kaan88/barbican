// Mirrors: Rust sieve.rs:15 Parsed; Bulwark lib/sieve/parser.ts, lib/sieve/generator.ts (the @metadata block). M6 rows ported early because quick rules need them
// (M2 plan 2.1). Stub from stage F of M2; owner search.

using System.Text.Json.Nodes;

namespace Barbican.Core.Sieve;

/// <summary>A Sieve script split into Bulwark's metadata and everything else.</summary>
/// <param name="Metadata">The whole metadata object; save with <see cref="SieveScript.WithRules"/> so the extras are kept.</param>
/// <param name="External">Everything that is not Bulwark's (other clients' rules), kept verbatim.</param>
/// <param name="Locked">The metadata is something Bulwark itself would take for a hand-edited script: do not rewrite it.</param>
public sealed record SieveParsed(JsonNode Metadata, string External, bool Locked);
