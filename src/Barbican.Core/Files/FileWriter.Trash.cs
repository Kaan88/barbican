// Mirrors: Rust files.rs:465 delete (a delete of the user's own item is a move into a fresh batch folder of .Trash, created when missing; the batch is removed again when the move failed), :481 emptied_batches, :506 purge (the item and its emptied
// batch, onDestroyRemoveChildren), :515 restore (the batch's parent when it is still a folder outside the trash, else the root; a free name), :539 empty_trash (the folder goes with all of it); Bulwark lib/files-trash.ts,
// stores/file-store.ts (onBeforeFileDelete :1406 / onAfterFileDelete :1411); Stalwart crates/jmap/src/file/set.rs (destroy removes a node and everything below it for good: there is no server trash). B-FILE-007..009, B-FILE-011. Owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Files;

public sealed partial class FileWriter
{
    /// <inheritdoc/>
    public async Task<Result<Unit>> TrashAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<Unit>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var picked = ctx.TopLevel([.. keys.Select(ctx.Find).OfType<FileEntry>().DistinctBy(e => e.Key, StringComparer.Ordinal)]);
        if (picked.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        // never a destroy: something already in the trash, in a shared account (which has no trash) or the trash itself is deleted for good only by PurgeAsync, which the caller asks first (B-FILE-011, D-M5-10)
        if (picked.FirstOrDefault(e => e.Shared || ctx.Trash.Contains(e.Key)) is { } permanent)
        {
            return Result<Unit>.Fail(new ValidationError($"\"{permanent.Name}\" cannot be moved to the trash; deleting it is for good."));
        }

        var views = picked.Select(ctx.View).ToList();
        if (!await _hooks.AllowAsync(HookNames.OnBeforeFileDelete, () => new FileListPayload(views), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(new ValidationError("A plugin stopped the delete."));
        }

        var failure = await MoveToTrashAsync(ctx, picked, ct).ConfigureAwait(false);
        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        if (failure is not null)
        {
            return Result<Unit>.Fail(failure);
        }

        _hooks.Fire(HookNames.OnAfterFileDelete, () => new FileListPayload(views));
        return Result<Unit>.Ok(default);
    }

    // the items into fresh dated batch folders of .Trash (made when missing), one batch per folder they came from; the first failure after every group was tried
    private async Task<JmapError?> MoveToTrashAsync(FileContext ctx, IReadOnlyList<FileEntry> picked, CancellationToken ct)
    {
        var trashId = FileTrash.Folder(ctx.Entries)?.Id;
        if (trashId is null)
        {
            var made = await FileJmap.CreateAsync(ctx.Client, ctx.Draft, ctx.Primary, null, FileTrash.TrashName, null, null, ct).ConfigureAwait(false);
            if (!made.IsOk)
            {
                return made.Error;
            }

            trashId = made.Value!;
        }

        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        JmapError? failure = null;
        foreach (var group in picked.GroupBy(e => e.ParentKey, StringComparer.Ordinal))
        {
            var batch = await FileJmap.CreateAsync(ctx.Client, ctx.Draft, ctx.Primary, trashId, FileTrash.BatchName(group.Key, now), null, null, ct).ConfigureAwait(false);
            if (!batch.IsOk)
            {
                failure ??= batch.Error;
                continue;
            }

            var moved = await UpdateParentsAsync(ctx, ctx.Primary, [.. group], batch.Value, ct).ConfigureAwait(false);
            if (moved is not null)
            {
                failure ??= moved;

                // the batch folder stays empty only when nothing moved into it
                _ = await FileJmap.SetAsync(ctx.Client, ctx.Primary, new JsonObject { ["destroy"] = new JsonArray(batch.Value) }, ct).ConfigureAwait(false);
            }
        }

        return failure;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> RestoreAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<Unit>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        var items = FileTrash.Items(ctx.Entries).Where(t => wanted.Contains(t.Entry.Key)).ToList();
        if (items.Count == 0)
        {
            return Result<Unit>.Fail(new ValidationError("Nothing to restore: it is not in the trash."));
        }

        var plan = RestorePlan(ctx, items);
        JmapError? failure = null;
        foreach (var chunk in plan.Chunk(ctx.Client.Session.Limits.MaxObjectsInSet))
        {
            var update = new JsonObject();
            foreach (var (target, entry, name) in chunk)
            {
                var patch = new JsonObject { ["parentId"] = target };
                if (!string.Equals(name, entry.Name, StringComparison.Ordinal))
                {
                    patch["name"] = name;
                }

                update[entry.Id] = patch;
            }

            var set = FileJmap.Whole(await FileJmap.SetAsync(ctx.Client, ctx.Primary, new JsonObject { ["update"] = update }, ct).ConfigureAwait(false));
            failure ??= set.IsOk ? null : set.Error;
        }

        if (failure is null)
        {
            var emptied = FileTrash.EmptiedBatches(ctx.Entries, [.. items.Select(t => t.Entry.Key)]);
            if (emptied.Count > 0)
            {
                _ = await FileJmap.SetAsync(ctx.Client, ctx.Primary, new JsonObject { ["destroy"] = new JsonArray([.. emptied.Select(b => (JsonNode)b)]) }, ct).ConfigureAwait(false);
            }
        }

        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        return failure is null ? Result<Unit>.Ok(default) : Result<Unit>.Fail(failure);
    }

    // back to the folder it came from while that is a folder outside the trash, else the root; under a name that is free there
    private static List<(string? Target, FileEntry Entry, string Name)> RestorePlan(FileContext ctx, IReadOnlyList<TrashedItem> items)
    {
        var used = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var plan = new List<(string? Target, FileEntry Entry, string Name)>();
        foreach (var item in items)
        {
            var target = item.FromParent is { } from && ctx.Find(from) is { Shared: false, IsDirectory: true } && !ctx.Trash.Contains(from) ? from : null;
            if (!used.TryGetValue(target ?? string.Empty, out var names))
            {
                used[target ?? string.Empty] = names = ctx.Names(target, ctx.Primary);
            }

            var free = FileNames.UniqueName(item.Entry.Name, names);
            _ = names.Add(free);
            plan.Add((target, item.Entry, free));
        }

        return plan;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> PurgeAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<Unit>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var picked = ctx.TopLevel([.. keys.Select(ctx.Find).OfType<FileEntry>().DistinctBy(e => e.Key, StringComparer.Ordinal)]);
        if (picked.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        if (picked.FirstOrDefault(e => string.Equals(e.Key, FileTrash.Folder(ctx.Entries)?.Key, StringComparison.Ordinal)) is not null)
        {
            return Result<Unit>.Fail(new ValidationError("Use Empty trash to delete the trash itself."));
        }

        var views = picked.Select(ctx.View).ToList();
        if (!await _hooks.AllowAsync(HookNames.OnBeforeFileDelete, () => new FileListPayload(views), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(new ValidationError("A plugin stopped the delete."));
        }

        // the batch folders this empties go too
        var destroy = picked.Select(e => e).ToList();
        foreach (var batch in FileTrash.EmptiedBatches(ctx.Entries, [.. picked.Select(e => e.Key)]))
        {
            if (ctx.Find(batch) is { } b && destroy.TrueForAll(d => !string.Equals(d.Key, b.Key, StringComparison.Ordinal)))
            {
                destroy.Add(b);
            }
        }

        var failure = await DestroyAsync(ctx, destroy, ct).ConfigureAwait(false);
        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        if (failure is not null)
        {
            return Result<Unit>.Fail(failure);
        }

        _hooks.Fire(HookNames.OnAfterFileDelete, () => new FileListPayload(views));
        return Result<Unit>.Ok(default);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> EmptyTrashAsync(string accountKey, CancellationToken ct)
    {
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<Unit>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        if (FileTrash.Folder(ctx.Entries) is not { } trash)
        {
            return Result<Unit>.Ok(default);
        }

        var views = FileTrash.Items(ctx.Entries).Select(t => ctx.View(t.Entry)).ToList();
        if (!await _hooks.AllowAsync(HookNames.OnBeforeFileDelete, () => new FileListPayload(views), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(new ValidationError("A plugin stopped the delete."));
        }

        // the folder goes with all of it; the next delete makes a new one
        var failure = await DestroyAsync(ctx, [trash], ct).ConfigureAwait(false);
        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        if (failure is not null)
        {
            return Result<Unit>.Fail(failure);
        }

        if (views.Count > 0)
        {
            _hooks.Fire(HookNames.OnAfterFileDelete, () => new FileListPayload(views));
        }

        return Result<Unit>.Ok(default);
    }

    // destroys nodes with everything below them, per account, in chunks; the first failure after every chunk was tried
    private static async Task<JmapError?> DestroyAsync(FileContext ctx, IReadOnlyList<FileEntry> entries, CancellationToken ct)
    {
        JmapError? first = null;
        foreach (var byAccount in entries.GroupBy(ctx.AccountOf, StringComparer.Ordinal))
        {
            foreach (var chunk in byAccount.Chunk(ctx.Client.Session.Limits.MaxObjectsInSet))
            {
                var body = new JsonObject { ["destroy"] = new JsonArray([.. chunk.Select(e => (JsonNode)e.Id)]), ["onDestroyRemoveChildren"] = true };
                var set = FileJmap.Whole(await FileJmap.SetAsync(ctx.Client, byAccount.Key, body, ct).ConfigureAwait(false));
                first ??= set.IsOk ? null : set.Error;
            }
        }

        return first;
    }
}
