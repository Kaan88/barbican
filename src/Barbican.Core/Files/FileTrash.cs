// Mirrors: Rust files.rs:73 TRASH_NAME, :75 TRASH_LOC, :78 trash_batch_name, :81 parse_trash_batch, :90 trash_subtree, :106 trashed_items, emptied_batches; Bulwark lib/files-trash.ts (.Trash with dated batches). B-FILE-007, B-FILE-008.
// Contract from the M5 plan section 4.6; owner files-data.

using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>The Bulwark trash: a ".Trash" folder in the primary account's root, one dated folder per delete.</summary>
public static class FileTrash
{
    public const string TrashName = ".Trash";

    /// <summary>The trash as a place in the Files view (no JMAP id contains "*").</summary>
    public const string TrashLocation = "*trash";

    /// <summary>"time~parent": the folder one delete's items go into (parent null: "time~").</summary>
    public static string BatchName(string? parentId, long deletedMs) => throw new PortPendingException("FileTrash.BatchName");

    /// <summary>When a batch was deleted (ms) and from which folder (null: the root); null for other names: fewer than 10 digits, a parent with characters other than letters, digits, "_" and "-", no "~".</summary>
    public static (long DeletedMs, string? Parent)? ParseBatch(string name) => throw new PortPendingException("FileTrash.ParseBatch");

    /// <summary>The trash folder (primary account, root, a directory named ".Trash") and everything below it, as keys; empty without a trash folder.</summary>
    public static ISet<string> Subtree(IReadOnlyList<FileEntry> entries) => throw new PortPendingException("FileTrash.Subtree");

    /// <summary>The items in the trash: the children of every batch folder (with when and from where) and loose children of the trash folder.</summary>
    public static IReadOnlyList<TrashedItem> Items(IReadOnlyList<FileEntry> entries) => throw new PortPendingException("FileTrash.Items");

    /// <summary>The batch folders that become empty when <paramref name="removedKeys"/> go (so they are removed too).</summary>
    public static IReadOnlyList<string> EmptiedBatches(IReadOnlyList<FileEntry> entries, IReadOnlyList<string> removedKeys) => throw new PortPendingException("FileTrash.EmptiedBatches");
}
