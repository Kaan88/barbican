// Mirrors: Rust files.rs:73 TRASH_NAME, :75 TRASH_LOC, :78 trash_batch_name, :81 parse_trash_batch, :90 trash_subtree, :106 trashed_items, emptied_batches; Bulwark lib/files-trash.ts (.Trash with dated batches). B-FILE-007, B-FILE-008.
// Contract from the M5 plan section 4.6; owner files-data.

using System.Globalization;

namespace Barbican.Core.Files;

/// <summary>The Bulwark trash: a ".Trash" folder in the primary account's root, one dated folder per delete.</summary>
public static class FileTrash
{
    public const string TrashName = ".Trash";

    /// <summary>The trash as a place in the Files view (no JMAP id contains "*").</summary>
    public const string TrashLocation = "*trash";

    /// <summary>"time~parent": the folder one delete's items go into (parent null: "time~").</summary>
    public static string BatchName(string? parentId, long deletedMs) => string.Create(CultureInfo.InvariantCulture, $"{deletedMs}~{parentId}");

    /// <summary>When a batch was deleted (ms) and from which folder (null: the root); null for other names: fewer than 10 digits, a parent with characters other than letters, digits, "_" and "-", no "~".</summary>
    public static (long DeletedMs, string? Parent)? ParseBatch(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var tilde = name.IndexOf('~', StringComparison.Ordinal);
        if (tilde < 0)
        {
            return null;
        }

        var ms = name[..tilde];
        var parent = name[(tilde + 1)..];
        if (ms.Length < 10 || !ms.All(char.IsAsciiDigit) || !parent.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            return null;
        }

        return long.TryParse(ms, NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? (t, parent.Length == 0 ? null : parent) : null;
    }

    /// <summary>The trash folder of a node list: primary account, root, a directory named ".Trash"; null when there is none.</summary>
    public static FileEntry? Folder(IReadOnlyList<FileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.FirstOrDefault(n => !n.Shared && n.ParentKey is null && n.IsDirectory && string.Equals(n.Name, TrashName, StringComparison.Ordinal));
    }

    /// <summary>The trash folder (primary account, root, a directory named ".Trash") and everything below it, as keys; empty without a trash folder.</summary>
    public static ISet<string> Subtree(IReadOnlyList<FileEntry> entries)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (Folder(entries) is not { } trash)
        {
            return ids;
        }

        var children = ChildrenByParent(entries);
        var queue = new Queue<string>();
        _ = ids.Add(trash.Key);
        queue.Enqueue(trash.Key);
        while (queue.Count > 0)
        {
            if (!children.TryGetValue(queue.Dequeue(), out var kids))
            {
                continue;
            }

            foreach (var k in kids.Where(k => ids.Add(k.Key)))
            {
                queue.Enqueue(k.Key);
            }
        }

        return ids;
    }

    /// <summary>The items in the trash: the children of every batch folder (with when and from where) and loose children of the trash folder.</summary>
    public static IReadOnlyList<TrashedItem> Items(IReadOnlyList<FileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (Folder(entries) is not { } trash)
        {
            return [];
        }

        var children = ChildrenByParent(entries);
        var output = new List<TrashedItem>();
        foreach (var child in children.GetValueOrDefault(trash.Key) ?? [])
        {
            if (child.IsDirectory && ParseBatch(child.Name) is { } batch)
            {
                output.AddRange((children.GetValueOrDefault(child.Key) ?? []).Select(n => new TrashedItem(n, child.Key, batch.DeletedMs, batch.Parent)));
            }
            else
            {
                output.Add(new TrashedItem(child, null, null, null));
            }
        }

        return output;
    }

    /// <summary>The batch folders that become empty when <paramref name="removedKeys"/> go (so they are removed too).</summary>
    public static IReadOnlyList<string> EmptiedBatches(IReadOnlyList<FileEntry> entries, IReadOnlyList<string> removedKeys)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(removedKeys);
        var gone = new HashSet<string>(removedKeys, StringComparer.Ordinal);
        var children = ChildrenByParent(entries);
        var output = new List<string>();
        foreach (var item in Items(entries).Where(t => gone.Contains(t.Entry.Key)))
        {
            if (item.BatchKey is not { } batch || output.Contains(batch, StringComparer.Ordinal))
            {
                continue;
            }

            if ((children.GetValueOrDefault(batch) ?? []).All(n => gone.Contains(n.Key)))
            {
                output.Add(batch);
            }
        }

        return output;
    }

    private static Dictionary<string, List<FileEntry>> ChildrenByParent(IReadOnlyList<FileEntry> entries)
    {
        var map = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);
        foreach (var n in entries.Where(n => n.ParentKey is not null))
        {
            if (!map.TryGetValue(n.ParentKey!, out var list))
            {
                map[n.ParentKey!] = list = [];
            }

            list.Add(n);
        }

        return map;
    }
}
