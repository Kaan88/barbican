// Mirrors: Rust quick_rules.rs:75 sieve_path (the (id, name, parentId, role) tuples of the mailbox list). Stub from stage F of M2; owner search.

namespace Barbican.Core.Filters.QuickRules;

/// <summary>A mailbox as the Sieve path builder sees it.</summary>
public sealed record SieveFolderRef(string Id, string Name, string? ParentId, string Role);
