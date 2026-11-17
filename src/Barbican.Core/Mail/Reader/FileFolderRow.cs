// Mirrors: Rust reader.rs:887 folder_tree (the (id, name, depth) triples of the Save to Files folder list); Bulwark components/email/save-to-files-dialog.tsx.
// Stub from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Reader;

/// <summary>One folder of the Files tree as the Save to Files dialog lists it.</summary>
public sealed record FileFolderRow(string Id, string Name, int Depth);
