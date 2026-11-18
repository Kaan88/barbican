// Mirrors: Rust files.rs:106 trashed_items (item, its batch key, deleted at ms, the folder it came from). Contract from the M5 plan section 4.6; owner files-data.

namespace Barbican.Core.Files;

/// <summary>What the trash holds: the item, the key of its dated batch folder (null for a loose item), when it was deleted (UTC ms) and the key of the folder it came from (null: the root).</summary>
public sealed record TrashedItem(FileEntry Entry, string? BatchKey, long? DeletedMs, string? FromParent);
