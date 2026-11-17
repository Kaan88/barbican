// Mirrors: Rust sieve.rs:82 Block (a top-level statement of a script with its raw text); Bulwark lib/sieve/parser.ts. Stub from stage F of M2; owner search.

namespace Barbican.Core.Sieve;

/// <summary>One top-level block of a Sieve script.</summary>
/// <param name="Kind">What it is (an if block, a command, a comment run).</param>
/// <param name="Raw">The block's text as written, comments and string bodies included.</param>
/// <param name="Statement">The block without leading comments.</param>
public sealed record SieveBlock(string Kind, string Raw, string Statement);
