// Mirrors: Rust files.rs:652 paste (copy a node and its subtree into a folder; a file in another account has its blob moved over), :638 copy_tree (a folder with everything below it; files reuse their blob in the same account), :684 duplicate
// ("name (copy).ext" next to the original); Bulwark stores/file-store.ts (pasteResources :1718 with onBeforeFileMove and onFileCopy :1746, duplicateResource :1554 with onFileDuplicate); RFC 8620 6.3 (Blob/copy: one call moves the blobs
// of a level into the other account, J-036); Stalwart crates/jmap/src/blob/copy.rs, crates/jmap/src/file/set.rs (onExists rename). B-FILE-006. Owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Files;

public sealed partial class FileWriter
{
    /// <summary>The most nodes one copy makes (a hostile tree must not run the client out of memory or the server out of requests).</summary>
    public const int MaxCopyNodes = 100_000;

    private const int BlobCopyChunk = 100;

    /// <inheritdoc/>
    public async Task<Result<Unit>> CopyAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<Unit>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var target = ctx.Target(destinationKey);
        if (!target.IsOk)
        {
            return Result<Unit>.Fail(target.Error!);
        }

        var (account, parentId) = target.Value;
        var picked = ctx.TopLevel([.. keys.Select(ctx.Find).OfType<FileEntry>().DistinctBy(e => e.Key, StringComparer.Ordinal)]);
        if (picked.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        foreach (var e in picked)
        {
            if (string.Equals(e.Name, FileTrash.TrashName, StringComparison.Ordinal) && e.ParentKey is null && !e.Shared)
            {
                return Result<Unit>.Fail(new ValidationError("The trash cannot be copied."));
            }

            if (destinationKey is not null && ctx.Subtree(e).Any(s => string.Equals(s.Key, destinationKey, StringComparison.Ordinal)))
            {
                return Result<Unit>.Fail(new ValidationError("A folder cannot be copied into itself."));
            }
        }

        var toPath = FilePaths.Of(ctx.Entries, destinationKey);
        var views = picked.Select(ctx.View).ToList();
        if (!await _hooks.AllowAsync(HookNames.OnBeforeFileMove, () => new FileMovePayload(views, toPath), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(new ValidationError("A plugin stopped the copy."));
        }

        var used = ctx.Names(destinationKey, account);
        var tops = new List<(FileEntry, string)>();
        foreach (var e in picked)
        {
            var name = FileNames.UniqueName(e.Name, used);
            _ = used.Add(name);
            tops.Add((e, name));
        }

        var copied = await CopyTreeAsync(ctx, tops, account, parentId, ct).ConfigureAwait(false);
        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        if (!copied.IsOk)
        {
            return Result<Unit>.Fail(copied.Error!);
        }

        _hooks.Fire(HookNames.OnFileCopy, () => new FileMovePayload(views, toPath));
        return Result<Unit>.Ok(default);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> DuplicateAsync(string accountKey, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<Unit>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        if (ctx.Find(key) is not { } entry)
        {
            return Result<Unit>.Fail(new ValidationError("It is not there any more."));
        }

        if (ctx.Trash.Contains(key))
        {
            return Result<Unit>.Fail(new ValidationError("Items of the trash cannot be duplicated."));
        }

        var account = ctx.AccountOf(entry);
        var name = FileNames.UniqueName(FileNames.CopyName(entry.Name, entry.IsDirectory), ctx.Names(entry.ParentKey, account));
        var copied = await CopyTreeAsync(ctx, [(entry, name)], account, ctx.RawParent(entry), ct).ConfigureAwait(false);
        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        if (!copied.IsOk)
        {
            return Result<Unit>.Fail(copied.Error!);
        }

        var newKey = FileEntry.MakeKey(account, copied.Value![0], !string.Equals(account, ctx.Primary, StringComparison.Ordinal));
        if (_store.Snapshot(accountKey) is { } snap && snap.Entries.FirstOrDefault(e => string.Equals(e.Key, newKey, StringComparison.Ordinal)) is { } made)
        {
            _hooks.Fire(HookNames.OnFileDuplicate, () => FileResourceView.From(made, FilePaths.Of(snap.Entries, made.ParentKey)));
        }

        return Result<Unit>.Ok(default);
    }

    /// <summary>Copies the tops and everything below them into a folder, one level (and one /set) at a time, parents first; the blobs of a level come over with one Blob/copy when the account differs. The raw ids of the new tops, in order.</summary>
    private static async Task<Result<IReadOnlyList<string>>> CopyTreeAsync(FileContext ctx, List<(FileEntry Source, string Name)> tops, string destAccount, string? destParent, CancellationToken ct)
    {
        var byParent = ctx.Entries.Where(e => e.ParentKey is not null).GroupBy(e => e.ParentKey!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var topIds = new string[tops.Count];
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var level = tops.Select((t, i) => new Item(t.Source, t.Name, destParent, i)).ToList();
        var total = 0;
        while (level.Count > 0)
        {
            level = [.. level.Where(i => visited.Add(i.Source.Key) && (i.Source.IsDirectory || i.Source.BlobId is not null))];
            total += level.Count;
            if (total > MaxCopyNodes)
            {
                return Result<IReadOnlyList<string>>.Fail(new ValidationError("That is too many files to copy at once."));
            }

            var blobs = await BlobsAsync(ctx, level, destAccount, ct).ConfigureAwait(false);
            if (!blobs.IsOk)
            {
                return Result<IReadOnlyList<string>>.Fail(blobs.Error!);
            }

            var next = new List<Item>();
            foreach (var chunk in level.Chunk(ctx.Client.Session.Limits.MaxObjectsInSet))
            {
                var create = new JsonObject();
                for (var i = 0; i < chunk.Length; i++)
                {
                    var item = chunk[i];
                    create["c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = FileJmap.NodeBody(ctx.Draft, item.ParentId, item.Name, item.Source.IsDirectory ? null : blobs.Value![item.Source.Key], item.Source.MediaType);
                }

                var set = FileJmap.Whole(await FileJmap.SetAsync(ctx.Client, destAccount, new JsonObject { ["create"] = create, ["onExists"] = "rename" }, ct).ConfigureAwait(false));
                if (!set.IsOk)
                {
                    return Result<IReadOnlyList<string>>.Fail(set.Error!);
                }

                for (var i = 0; i < chunk.Length; i++)
                {
                    if (!set.Value!.Created.TryGetValue("c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), out var id))
                    {
                        return Result<IReadOnlyList<string>>.Fail(new ValidationError("The server created nothing."));
                    }

                    if (chunk[i].Top >= 0)
                    {
                        topIds[chunk[i].Top] = id;
                    }

                    if (chunk[i].Source.IsDirectory)
                    {
                        next.AddRange((byParent.GetValueOrDefault(chunk[i].Source.Key) ?? []).Select(c => new Item(c, c.Name, id, -1)));
                    }
                }
            }

            level = next;
        }

        return Result<IReadOnlyList<string>>.Ok(topIds);
    }

    // the blob id each file of the level gets in the destination account: its own when the account is the same, else the answer of Blob/copy
    private static async Task<Result<Dictionary<string, string>>> BlobsAsync(FileContext ctx, List<Item> level, string destAccount, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var bySource in level.Where(i => !i.Source.IsDirectory).GroupBy(i => ctx.AccountOf(i.Source), StringComparer.Ordinal))
        {
            if (string.Equals(bySource.Key, destAccount, StringComparison.Ordinal))
            {
                foreach (var i in bySource)
                {
                    map[i.Source.Key] = i.Source.BlobId!;
                }

                continue;
            }

            foreach (var chunk in bySource.Chunk(BlobCopyChunk))
            {
                var ids = chunk.Select(i => i.Source.BlobId!).Distinct(StringComparer.Ordinal).ToList();
                var args = new JsonObject { ["fromAccountId"] = bySource.Key, ["accountId"] = destAccount, ["blobIds"] = new JsonArray([.. ids.Select(b => (JsonNode)b)]) };
                var r = await ctx.Client.CallAsync(JmapRequest.Of(JmapRequest.Call("Blob/copy", args, "b")), ct).ConfigureAwait(false);
                var res = r.IsOk ? r.Value!.Args("b", "Blob/copy") : Result<JsonObject>.Fail(r.Error!);
                if (!res.IsOk)
                {
                    return Result<Dictionary<string, string>>.Fail(res.Error!);
                }

                foreach (var i in chunk)
                {
                    var copied = BlobCopyRequests.Copied(res.Value!, i.Source.BlobId!);
                    if (!copied.IsOk)
                    {
                        return Result<Dictionary<string, string>>.Fail(copied.Error!);
                    }

                    map[i.Source.Key] = copied.Value!;
                }
            }
        }

        return Result<Dictionary<string, string>>.Ok(map);
    }

    private sealed record Item(FileEntry Source, string Name, string? ParentId, int Top);
}
