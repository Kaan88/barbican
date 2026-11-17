// Mirrors: Rust gsearch.rs:314 chips ((operator key, label, value)); Bulwark components/search/ (the removable chips). Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>A removable chip of a mail search.</summary>
public sealed record SearchChip(string Key, string Label, string Value);
