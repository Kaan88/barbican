// Mirrors: Rust gsearch.rs:62 Token; Bulwark lib/global-search/query-parser.ts TOKEN_RE.

namespace Barbican.Core.Search;

/// <summary>One token of a search string: an operator with its value, a quoted phrase or a bare word.</summary>
/// <param name="Key">The lower-cased operator key, or null for text.</param>
/// <param name="Value">The value (quotes removed).</param>
/// <param name="Quoted">Whether the value was quoted.</param>
/// <param name="Original">The token as typed.</param>
public sealed record SearchToken(string? Key, string Value, bool Quoted, string Original);
