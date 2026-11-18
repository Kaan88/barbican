// Mirrors: Rust files.rs State (nodes of every account with the filenode capability). Contract from the M5 plan section 4.7; owner files-data.

namespace Barbican.Core.Files;

/// <summary>Everything the Files app knows of one login: the nodes of its own and shared accounts, the name rules, the state string for /changes.</summary>
public sealed record FileSnapshot(string AccountKey, IReadOnlyList<FileEntry> Entries, FileNameRules? Rules, string? State);
