// Mirrors: Rust files.rs:409 new_folder, :451 rename, :619 move_to (onExists rename), :743 share (shareWith/<principal>, the first SetError, "The server did not confirm the share"); Bulwark stores/file-store.ts (createFolder :1086 with onDirectoryCreate,
// renameResource :1457 with onBeforeFileRename / onFileRename, moveResource :1658 with onBeforeFileMove / onFileMove), lib/file-name-rules.ts (a name is checked before the request); draft-ietf-jmap-filenode FileNode/set; Stalwart
// crates/jmap/src/file/set.rs. B-FILE-002, B-FILE-006, B-FILE-009..011. Owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Files;

/// <summary>The default <see cref="IFileWriter"/>: every write goes through JMAP, then the store reads /changes so it is consistent.</summary>
public sealed partial class FileWriter : IFileWriter
{
    private readonly IAccountManager _accounts;
    private readonly IFileStore _store;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;

    public FileWriter(IAccountManager accounts, IFileStore store, IHookBus hooks, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hooks);
        _accounts = accounts;
        _store = store;
        _hooks = hooks;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateFolderAsync(string accountKey, string? parentKey, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<string>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var target = ctx.Target(parentKey);
        if (!target.IsOk)
        {
            return Result<string>.Fail(target.Error!);
        }

        var clean = name.Trim();
        if (NameRefusal(ctx, clean) is { } refusal)
        {
            return Result<string>.Fail(refusal);
        }

        var (account, parentId) = target.Value;
        var unique = FileNames.UniqueName(clean, ctx.Names(parentKey, account));
        var made = await FileJmap.CreateAsync(ctx.Client, ctx.Draft, account, parentId, unique, null, null, ct).ConfigureAwait(false);
        if (!made.IsOk)
        {
            return Result<string>.Fail(made.Error!);
        }

        var key = FileEntry.MakeKey(account, made.Value!, !string.Equals(account, ctx.Primary, StringComparison.Ordinal));
        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        if (_store.Snapshot(accountKey)?.Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal)) is { } entry)
        {
            _hooks.Fire(HookNames.OnDirectoryCreate, () => FileResourceView.From(entry, FilePaths.Of(_store.Snapshot(accountKey)!.Entries, entry.ParentKey)));
        }

        return Result<string>.Ok(key);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> RenameAsync(string accountKey, string key, string newName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(newName);
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

        var clean = newName.Trim();
        if (clean.Length == 0 || string.Equals(clean, entry.Name, StringComparison.Ordinal))
        {
            return Result<Unit>.Ok(default);
        }

        if (string.Equals(entry.Name, FileTrash.TrashName, StringComparison.Ordinal) && entry.ParentKey is null && !entry.Shared)
        {
            return Result<Unit>.Fail(new ValidationError("The trash cannot be renamed."));
        }

        if (NameRefusal(ctx, clean) is { } refusal)
        {
            return Result<Unit>.Fail(refusal);
        }

        var account = ctx.AccountOf(entry);
        if (ctx.Names(entry.ParentKey, account).Contains(clean))
        {
            return Result<Unit>.Fail(new ValidationError($"\"{clean}\" already exists in this folder."));
        }

        var view = ctx.View(entry);
        if (!await _hooks.AllowAsync(HookNames.OnBeforeFileRename, () => new FileRenamePayload(view, clean, null), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(new ValidationError("A plugin stopped the rename."));
        }

        var body = new JsonObject { ["update"] = new JsonObject { [entry.Id] = new JsonObject { ["name"] = clean } } };
        var set = FileJmap.Whole(await FileJmap.SetAsync(ctx.Client, account, body, ct).ConfigureAwait(false));
        if (!set.IsOk)
        {
            return Result<Unit>.Fail(set.Error!);
        }

        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        _hooks.Fire(HookNames.OnFileRename, () => new FileRenamePayload(view with { Name = clean }, null, entry.Name));
        return Result<Unit>.Ok(default);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct)
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
        foreach (var e in picked)
        {
            if (ctx.Trash.Contains(e.Key))
            {
                return Result<Unit>.Fail(new ValidationError("Restore an item of the trash instead of moving it."));
            }

            if (!string.Equals(ctx.AccountOf(e), account, StringComparison.Ordinal))
            {
                return Result<Unit>.Fail(new ValidationError("Items cannot move to another account; copy them instead."));
            }

            if (destinationKey is not null && ctx.Subtree(e).Any(s => string.Equals(s.Key, destinationKey, StringComparison.Ordinal)))
            {
                return Result<Unit>.Fail(new ValidationError("A folder cannot move into itself."));
            }
        }

        var moving = picked.Where(e => !string.Equals(e.ParentKey, destinationKey, StringComparison.Ordinal)).ToList();
        if (moving.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var toPath = FilePaths.Of(ctx.Entries, destinationKey);
        var views = moving.Select(ctx.View).ToList();
        if (!await _hooks.AllowAsync(HookNames.OnBeforeFileMove, () => new FileMovePayload(views, toPath), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(new ValidationError("A plugin stopped the move."));
        }

        var failure = await UpdateParentsAsync(ctx, account, moving, parentId, ct).ConfigureAwait(false);
        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        if (failure is not null)
        {
            return Result<Unit>.Fail(failure);
        }

        _hooks.Fire(HookNames.OnFileMove, () => new FileMovePayload(views, toPath));
        return Result<Unit>.Ok(default);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ShareAsync(string accountKey, string key, string principalId, bool? write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(principalId);
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

        if (principalId.Length == 0)
        {
            return Result<Unit>.Fail(new ValidationError("Choose who to share with."));
        }

        var account = ctx.AccountOf(entry);
        var legacy = FileAccounts.IsLegacy(ctx.Client.Session, account);
        var patch = new JsonObject { [FileJmap.ShareKey(principalId)] = write is { } w ? FileJmap.Rights(legacy, w) : null };
        var body = new JsonObject { ["update"] = new JsonObject { [entry.Id] = patch } };
        var set = FileJmap.Whole(await FileJmap.SetAsync(ctx.Client, account, body, ct).ConfigureAwait(false));
        if (!set.IsOk)
        {
            return Result<Unit>.Fail(set.Error!);
        }

        if (!set.Value!.Updated.Contains(entry.Id, StringComparer.Ordinal))
        {
            return Result<Unit>.Fail(new ValidationError("The server did not confirm the share."));
        }

        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(default);
    }

    /// <summary>Moves nodes under one parent, in chunks of maxObjectsInSet; a name taken is made unique (onExists rename). The first failure, after every chunk was tried.</summary>
    private static async Task<JmapError?> UpdateParentsAsync(FileContext ctx, string account, IReadOnlyList<FileEntry> entries, string? parentId, CancellationToken ct)
    {
        JmapError? first = null;
        foreach (var chunk in entries.Chunk(ctx.Client.Session.Limits.MaxObjectsInSet))
        {
            var update = new JsonObject();
            foreach (var e in chunk)
            {
                update[e.Id] = new JsonObject { ["parentId"] = parentId };
            }

            var set = FileJmap.Whole(await FileJmap.SetAsync(ctx.Client, account, new JsonObject { ["update"] = update, ["onExists"] = "rename" }, ct).ConfigureAwait(false));
            first ??= set.IsOk ? null : set.Error;
        }

        return first;
    }

    private static ValidationError? NameRefusal(FileContext ctx, string name)
    {
        if (name.Length == 0)
        {
            return new ValidationError("A name is needed.");
        }

        return FileNameChecks.ProblemOf(name, ctx.Snapshot.Rules) is { } problem ? new ValidationError(FileNameChecks.Message(problem)) : null;
    }
}
