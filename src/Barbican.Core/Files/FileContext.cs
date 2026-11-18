// Mirrors: Rust files.rs:226 target (the account and raw id a new node goes into for a folder key; "" is the primary root), :87 trash_folder, :194 keys; Bulwark stores/file-store.ts (the current account and parent of an operation). Owner files-data.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Files;

/// <summary>One login as an operation of the writer or the transfer sees it: the client, the snapshot at the start, the key arithmetic.</summary>
internal sealed class FileContext
{
    private readonly Dictionary<string, FileEntry> _byKey;
    private ISet<string>? _trash;

    private FileContext(string accountKey, IJmapClient client, FileSnapshot snapshot)
    {
        AccountKey = accountKey;
        Client = client;
        Snapshot = snapshot;
        Primary = FileAccounts.Primary(client.Session);
        _byKey = snapshot.Entries.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public string AccountKey { get; }

    public IJmapClient Client { get; }

    public FileSnapshot Snapshot { get; }

    /// <summary>The user's own file account.</summary>
    public string Primary { get; }

    /// <summary>True when creates carry @type (Stalwart 1.0).</summary>
    public bool Draft => Snapshot.Draft18;

    public IReadOnlyList<FileEntry> Entries => Snapshot.Entries;

    /// <summary>The keys of the trash folder and everything below it.</summary>
    public ISet<string> Trash => _trash ??= FileTrash.Subtree(Snapshot.Entries);

    /// <summary>Opens the login and its snapshot (loaded when the store has none yet).</summary>
    public static async Task<Result<FileContext>> OpenAsync(IAccountManager accounts, IFileStore store, string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(accountKey);
        if (accounts.SessionFor(accountKey) is not { } session)
        {
            return Result<FileContext>.Fail(new ValidationError("This account is not signed in."));
        }

        if (!session.Client.Session.Has(FileAccounts.Capability))
        {
            return Result<FileContext>.Fail(new ValidationError("This server has no file storage."));
        }

        var snapshot = store.Snapshot(accountKey);
        if (snapshot is null)
        {
            var loaded = await store.LoadAsync(accountKey, ct).ConfigureAwait(false);
            if (!loaded.IsOk)
            {
                return Result<FileContext>.Fail(loaded.Error!);
            }

            snapshot = loaded.Value!;
        }

        return Result<FileContext>.Ok(new FileContext(accountKey, session.Client, snapshot));
    }

    public FileEntry? Find(string? key) => key is not null && _byKey.TryGetValue(key, out var e) ? e : null;

    /// <summary>The JMAP account a node lives in.</summary>
    public string AccountOf(FileEntry e) => FileEntry.SplitKey(e.Key, Primary).AccountId;

    /// <summary>The raw id of the parent of a node (null: a root).</summary>
    public string? RawParent(FileEntry e) => e.ParentKey is null ? null : FileEntry.SplitKey(e.ParentKey, Primary).Id;

    /// <summary>The account and raw id a new node goes into for a folder key (null: the root of the user's own account); a failure for a key that is not a folder, or is in the trash.</summary>
    public Result<(string Account, string? ParentId)> Target(string? folderKey)
    {
        if (folderKey is null)
        {
            return Result<(string, string?)>.Ok((Primary, null));
        }

        if (string.Equals(folderKey, FileTrash.TrashLocation, StringComparison.Ordinal))
        {
            return Result<(string, string?)>.Fail(new ValidationError("Nothing can be added to the trash."));
        }

        if (Find(folderKey) is not { } folder || !folder.IsDirectory)
        {
            return Result<(string, string?)>.Fail(new ValidationError("That folder is not there any more."));
        }

        return Trash.Contains(folderKey)
            ? Result<(string, string?)>.Fail(new ValidationError("Nothing can be added to the trash."))
            : Result<(string, string?)>.Ok((AccountOf(folder), folder.Id));
    }

    /// <summary>The names in a folder (null key: the root of the user's own account), the trash left out.</summary>
    public HashSet<string> Names(string? parentKey, string account) =>
        new(Entries.Where(e => string.Equals(e.ParentKey, parentKey, StringComparison.Ordinal) && string.Equals(AccountOf(e), account, StringComparison.Ordinal)).Select(e => e.Name), StringComparer.Ordinal);

    /// <summary>The entries directly below a folder key.</summary>
    public IEnumerable<FileEntry> Children(string key) => Entries.Where(e => string.Equals(e.ParentKey, key, StringComparison.Ordinal));

    /// <summary>The entry and everything below it, parents first; a cycle in the server's data is cut.</summary>
    public IReadOnlyList<FileEntry> Subtree(FileEntry root)
    {
        var output = new List<FileEntry> { root };
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.Key };
        var byParent = Entries.Where(e => e.ParentKey is not null).GroupBy(e => e.ParentKey!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        for (var i = 0; i < output.Count; i++)
        {
            foreach (var c in byParent.GetValueOrDefault(output[i].Key) ?? [])
            {
                if (seen.Add(c.Key))
                {
                    output.Add(c);
                }
            }
        }

        return output;
    }

    /// <summary>Drops the entries that have an ancestor among the entries (a folder and a file in it: the folder moves it).</summary>
    public IReadOnlyList<FileEntry> TopLevel(IReadOnlyList<FileEntry> picked)
    {
        var keys = picked.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        return [.. picked.Where(e => !HasAncestorIn(e, keys))];
    }

    /// <summary>The hook view of an entry, with the path of its folder.</summary>
    public FileResourceView View(FileEntry e) => FileResourceView.From(e, FilePaths.Of(Entries, e.ParentKey));

    private bool HasAncestorIn(FileEntry e, HashSet<string> keys)
    {
        var cur = e.ParentKey;
        for (var i = 0; cur is not null && i < 64; i++)
        {
            if (keys.Contains(cur))
            {
                return true;
            }

            cur = Find(cur)?.ParentKey;
        }

        return false;
    }
}
