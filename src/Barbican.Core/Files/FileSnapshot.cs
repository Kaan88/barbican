// Mirrors: Rust files.rs State (nodes of every account with the filenode capability: nodes, draft18). Contract from the M5 plan section 4.7; owner files-data.

namespace Barbican.Core.Files;

/// <summary>Everything the Files app knows of one login: the nodes of its own and shared accounts, the name rules, the state string for /changes. <see cref="Draft18"/> is true for a server that wants @type on create (Stalwart 1.0).</summary>
public sealed record FileSnapshot(string AccountKey, IReadOnlyList<FileEntry> Entries, FileNameRules? Rules, string? State, bool Draft18 = false);
