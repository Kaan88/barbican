// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:61-130 (limits, filesAvailable, visibleNodes: everything but the Files trash and what is in it), :134-142 (isFolder, infoOf), :411-480 (ownDrive, placeOf, ownNode, ownFolder: writes only into
// the user's own drive, outside the trash, never in a folder shared with someone), lib/files-trash.ts. Stalwart crates/jmap/src/file/get.rs. Reads come from the store the Files view shows (loaded first when it never was).
using Barbican.Core.Accounts;
using Barbican.Core.Files;

namespace Barbican.Core.Plugins.Api.Files;

internal static class PluginFilesData
{
    /// <summary>Reads of a file's text or picture a plugin may make per minute (d.ts:71).</summary>
    internal const int MaxReadsPerMinute = 30;

    /// <summary>Changes to Files (create, createFolder, move, rename) a plugin may make per minute (d.ts:73).</summary>
    internal const int MaxWritesPerMinute = 20;

    internal const int MaxFindResults = 50;
    internal const int DefaultFindResults = 20;
    internal const int MaxListEntries = 200;

    /// <summary>The longest text files.create writes, in characters (d.ts:79).</summary>
    internal const int MaxCreateChars = 512 * 1024;

    /// <summary>The largest file read for its text or picture (lib/attachment-text/types.ts MAX_TEXT_SOURCE_BYTES).</summary>
    internal const int MaxReadBytes = 20 * 1024 * 1024;

    /// <summary>The login's nodes; loaded first when the store never was. Null when the login has no file storage or the load failed (d.ts:132-135 needFiles).</summary>
    internal static async Task<FileSnapshot?> SnapshotAsync(IPluginFilesHost host, IAccountSession session, CancellationToken ct)
    {
        if (!session.Client.Session.Has(FileAccounts.Capability)) return null;
        var key = session.Account.Key;
        if (host.Store.Snapshot(key) is { } known) return known;
        var loaded = await host.Store.LoadAsync(key, ct).ConfigureAwait(false);
        return loaded.IsOk ? loaded.Value : null;
    }

    /// <summary>d.ts:112-115 visibleNodes: everything but the Files trash and what is in it.</summary>
    internal static IReadOnlyList<FileEntry> Visible(FileSnapshot snapshot)
    {
        var trashed = FileTrash.Subtree(snapshot.Entries);
        return [.. snapshot.Entries.Where(e => !trashed.Contains(e.Key))];
    }

    internal static FileEntry? Find(IReadOnlyList<FileEntry> nodes, string key) => nodes.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));

    /// <summary>The folder a node is in from the top of its drive: "/" or "/Documents/2026" (pathOfNode).</summary>
    internal static string PathOf(IReadOnlyList<FileEntry> all, FileEntry node) => FilePaths.Of(all, node.ParentKey) is { Length: > 0 } path ? path : "/";

    /// <summary>The path of a folder itself, "/" for the top (pathOf, d.ts:466).</summary>
    internal static string PathOfFolder(IReadOnlyList<FileEntry> all, FileEntry? folder) => folder is null ? "/" : FilePaths.Of(all, folder.Key) is { Length: > 0 } path ? path : "/";

    /// <summary>The user's own drive as a write sees it: own nodes only (shared accounts left out), the trash marked.</summary>
    internal sealed class OwnDrive
    {
        internal OwnDrive(FileSnapshot snapshot)
        {
            All = snapshot.Entries;
            Nodes = [.. snapshot.Entries.Where(e => !e.Shared)];
            ById = Nodes.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Trashed = FileTrash.Subtree(snapshot.Entries);
            Rules = snapshot.Rules;
        }

        internal IReadOnlyList<FileEntry> All { get; }

        internal IReadOnlyList<FileEntry> Nodes { get; }

        internal Dictionary<string, FileEntry> ById { get; }

        internal ISet<string> Trashed { get; }

        internal FileNameRules? Rules { get; }

        /// <summary>d.ts:429-442 placeOf: "own", "shared" (the node or a folder above it is shared with someone) or "unknown" (a folder on the way up is not in the listing, which counts as shared).</summary>
        internal string PlaceOf(FileEntry node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (FileEntry? at = node; at is not null;)
            {
                if (!seen.Add(at.Key)) return "unknown";
                if (at.ShareWith is System.Text.Json.Nodes.JsonObject { Count: > 0 }) return "shared";
                if (at.ParentKey is null) return "own";
                at = ById.GetValueOrDefault(at.ParentKey);
            }

            return "unknown";
        }

        /// <summary>d.ts:445-453 ownNode: a node of the user's own drive, outside the trash, not in a shared folder; the reason it is not otherwise.</summary>
        internal (FileEntry? Node, Refusal Why) Own(string key)
        {
            if (!ById.TryGetValue(key, out var node) || Trashed.Contains(node.Key)) return (null, Refusal.Missing);
            return PlaceOf(node) switch
            {
                "own" => (node, Refusal.None),
                _ => (null, Refusal.Shared),
            };
        }

        /// <summary>d.ts:456-461 ownFolder: a folder of the user's own drive to write into (null key: the top).</summary>
        internal (FileEntry? Folder, Refusal Why) OwnFolder(string? key)
        {
            if (key is null) return (null, Refusal.None);
            var (node, why) = Own(key);
            if (node is null) return (null, why);
            return node.IsDirectory ? (node, Refusal.None) : (null, Refusal.NotFolder);
        }
    }

    internal enum Refusal
    {
        None,
        Missing,
        Shared,
        NotFolder,
    }
}
