// Mirrors: Bulwark components/settings/folder-settings.tsx (create, rename, move, delete, subscribe, icons :391-:735, count modes :735, notification mutes), stores/settings-store.ts (folderIcons, folderCountModes, notificationMutes), lib/notification-mutes.ts (the "accountId/mailboxId" key);
// RFC 8621 section 2.5 (Mailbox/set: parentId, name, isSubscribed, role, onDestroyRemoveEmails), section 4.6 (Email/set keywords); Stalwart crates/jmap/src/mailbox/set.rs (one folder per role, role folders are not renamed or moved, mailboxHasChild). G-034..G-041, I-428..I-430.
// Pin, mute, icon and count mode live in the Inbox metadata as the setting entries org.bulwarkmail.settings.value.{folder-icons, folder-count-modes, notification-mutes, pinned-folders} (D-M6-C03; the first three are Bulwark's, pinned-folders is Barbican's own, D-M6-C07). Owner srv-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Settings.Folders;

/// <summary>The default <see cref="IFolderAdmin"/>: every call runs on the pool (D-M6-00) and raises <see cref="Changed"/> after a write.</summary>
public sealed class FolderAdmin(IAccountManager accounts, TimeProvider time) : IFolderAdmin
{
    private const int Chunk = 100;

    /// <inheritdoc />
    public event EventHandler<FolderAdminChangedEventArgs>? Changed;

    /// <inheritdoc />
    public Task<Result<string>> CreateAsync(string accountKey, string? parentId, string name, CancellationToken ct) => Task.Run(() => CreateCoreAsync(accountKey, parentId, name, ct), ct);

    /// <inheritdoc />
    public Task<Result<Unit>> RenameAsync(string accountKey, string id, string name, CancellationToken ct) => Task.Run(() => RenameCoreAsync(accountKey, id, name, ct), ct);

    /// <inheritdoc />
    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> ids, string? parentId, CancellationToken ct) => Task.Run(() => MoveCoreAsync(accountKey, ids, parentId, ct), ct);

    /// <inheritdoc />
    public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct) => Task.Run(
        () => WriteAsync(accountKey, new JsonObject { ["destroy"] = new JsonArray(id), ["onDestroyRemoveEmails"] = true }, ct), ct);

    /// <inheritdoc />
    public Task<Result<Unit>> SubscribeAsync(string accountKey, string id, bool subscribed, CancellationToken ct) => Task.Run(
        () => WriteAsync(accountKey, new JsonObject { ["update"] = new JsonObject { [id] = new JsonObject { ["isSubscribed"] = subscribed } } }, ct), ct);

    /// <inheritdoc />
    public Task<Result<Unit>> SetRoleAsync(string accountKey, string id, string? role, CancellationToken ct) => Task.Run(() => SetRoleCoreAsync(accountKey, id, role, ct), ct);

    /// <inheritdoc />
    public Task<Result<Unit>> MarkTreeReadAsync(string accountKey, string id, CancellationToken ct) => Task.Run(() => MarkTreeReadCoreAsync(accountKey, id, ct), ct);

    /// <inheritdoc />
    public Task<Result<FolderAdminPrefs>> PrefsAsync(string accountKey, CancellationToken ct) => Task.Run(() => PrefsCoreAsync(accountKey, ct), ct);

    /// <inheritdoc />
    public Task<Result<Unit>> SetPrefAsync(string accountKey, string folderId, FolderAdminPref pref, string? value, CancellationToken ct) => Task.Run(() => SetPrefCoreAsync(accountKey, folderId, pref, value, ct), ct);

    private static IReadOnlyList<FolderAdminNode> Nodes(IReadOnlyList<JsonNode> boxes) =>
        [.. boxes.Select(b => new FolderAdminNode(b.Str("id") ?? string.Empty, b.Str("parentId"), b.Str("name") ?? string.Empty, b.Str("role")))];

    private static Dictionary<string, string> Strings(JsonNode? map) =>
        map is JsonObject o ? o.Where(kv => kv.Value is JsonValue v && v.TryGetValue<string>(out _)).ToDictionary(kv => kv.Key, kv => (string)kv.Value!, StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal);

    // the first per-object failure of a /set response in words, or null
    private static string? Problem(JsonObject response)
    {
        foreach (var map in new[] { "notCreated", "notUpdated", "notDestroyed" })
        {
            if (response[map] is JsonObject { Count: > 0 } failed)
            {
                var first = failed.First();
                return SetErrors.From(first.Key, first.Value!).Message;
            }
        }

        return null;
    }

    private static JsonObject MapWith(JsonNode? old, string key, string? value)
    {
        var map = old is JsonObject o ? (JsonObject)o.DeepClone() : [];
        if (value is null)
        {
            map.Remove(key);
        }
        else
        {
            map[key] = value;
        }

        return map;
    }

    private static JsonArray PinnedWith(JsonNode? old, string id, bool pinned)
    {
        var ids = (old as JsonArray)?.Select(x => x.AsString()).OfType<string>().Where(x => !string.Equals(x, id, StringComparison.Ordinal)).ToList() ?? [];
        if (pinned)
        {
            ids.Add(id);
        }

        return new JsonArray([.. ids.Select(x => (JsonNode?)JsonValue.Create(x))]);
    }

    private static JsonObject MutesWith(JsonNode? old, string key, bool? muted)
    {
        var accountsMuted = old?["accounts"] is JsonArray a ? (JsonArray)a.DeepClone() : [];
        var boxes = old?["mailboxes"] is JsonObject m ? (JsonObject)m.DeepClone() : [];
        if (muted is { } on)
        {
            boxes[key] = on;
        }
        else
        {
            boxes.Remove(key);
        }

        return new JsonObject { ["accounts"] = accountsMuted, ["mailboxes"] = boxes };
    }

    private async Task<Result<(IJmapClient Client, IReadOnlyList<FolderAdminNode> Folders)>> ContextAsync(string accountKey, CancellationToken ct)
    {
        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<(IJmapClient, IReadOnlyList<FolderAdminNode>)>.Fail(client.Error!);
        }

        var boxes = await FilterAccess.MailboxesAsync(client.Value!, ct).ConfigureAwait(false);
        return boxes.IsOk ? Result<(IJmapClient, IReadOnlyList<FolderAdminNode>)>.Ok((client.Value!, Nodes(boxes.Value!))) : Result<(IJmapClient, IReadOnlyList<FolderAdminNode>)>.Fail(boxes.Error!);
    }

    private async Task<Result<Unit>> WriteAsync(string accountKey, JsonObject set, CancellationToken ct)
    {
        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        set["accountId"] = client.Value!.Session.MailAccountId;
        var r = await ReaderCalls.CallAsync(client.Value, "Mailbox/set", set, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        if (Problem(r.Value!) is { } why)
        {
            return Result<Unit>.Fail(new ValidationError(why));
        }

        Changed?.Invoke(this, new FolderAdminChangedEventArgs(accountKey));
        return Result<Unit>.Ok(default);
    }

    private async Task<Result<string>> CreateCoreAsync(string accountKey, string? parentId, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        var ctx = await ContextAsync(accountKey, ct).ConfigureAwait(false);
        if (!ctx.IsOk)
        {
            return Result<string>.Fail(ctx.Error!);
        }

        var (client, folders) = ctx.Value;
        if (parentId is not null && !folders.Any(f => string.Equals(f.Id, parentId, StringComparison.Ordinal)))
        {
            return Result<string>.Fail(new ValidationError("The parent folder no longer exists."));
        }

        if (FolderAdminRules.NameProblem(name, [.. folders.Where(f => string.Equals(f.ParentId, parentId, StringComparison.Ordinal)).Select(f => f.Name)]) is { } why)
        {
            return Result<string>.Fail(new ValidationError(why));
        }

        var spec = new JsonObject { ["name"] = name.Trim(), ["isSubscribed"] = true };
        if (parentId is not null)
        {
            spec["parentId"] = parentId;
        }

        var r = await ReaderCalls.CallAsync(client, "Mailbox/set", new JsonObject { ["accountId"] = client.Session.MailAccountId, ["create"] = new JsonObject { ["f"] = spec } }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<string>.Fail(r.Error!);
        }

        var created = ReaderCalls.Created(r.Value!, "f");
        if (created.IsOk)
        {
            Changed?.Invoke(this, new FolderAdminChangedEventArgs(accountKey));
        }

        return created;
    }

    private async Task<Result<Unit>> RenameCoreAsync(string accountKey, string id, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        var ctx = await ContextAsync(accountKey, ct).ConfigureAwait(false);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        var folders = ctx.Value.Folders;
        var self = folders.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
        if (self is null)
        {
            return Result<Unit>.Fail(new ValidationError("The folder no longer exists."));
        }

        if (!string.IsNullOrEmpty(self.Role))
        {
            return Result<Unit>.Fail(new ValidationError("A system folder cannot be renamed."));
        }

        var siblings = folders.Where(f => string.Equals(f.ParentId, self.ParentId, StringComparison.Ordinal) && !string.Equals(f.Id, id, StringComparison.Ordinal)).Select(f => f.Name).ToList();
        if (FolderAdminRules.NameProblem(name, siblings) is { } why)
        {
            return Result<Unit>.Fail(new ValidationError(why));
        }

        return await WriteAsync(accountKey, new JsonObject { ["update"] = new JsonObject { [id] = new JsonObject { ["name"] = name.Trim() } } }, ct).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> MoveCoreAsync(string accountKey, IReadOnlyList<string> ids, string? parentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var ctx = await ContextAsync(accountKey, ct).ConfigureAwait(false);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        var folders = ctx.Value.Folders;
        var update = new JsonObject();
        foreach (var id in ids)
        {
            if (!FolderAdminRules.CanMoveUnder(folders, id, parentId))
            {
                var name = folders.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal))?.Name ?? id;
                return Result<Unit>.Fail(new ValidationError($"\"{name}\" cannot be moved there."));
            }

            update[id] = new JsonObject { ["parentId"] = parentId };
        }

        return update.Count == 0 ? Result<Unit>.Ok(default) : await WriteAsync(accountKey, new JsonObject { ["update"] = update }, ct).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> SetRoleCoreAsync(string accountKey, string id, string? role, CancellationToken ct)
    {
        if (role is not null && !FolderAdminRules.AssignableRoles.Contains(role, StringComparer.Ordinal))
        {
            return Result<Unit>.Fail(new ValidationError("That role cannot be given to a folder."));
        }

        var ctx = await ContextAsync(accountKey, ct).ConfigureAwait(false);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        var update = new JsonObject();

        // a role belongs to one folder: the previous holder loses it in the same request
        if (role is not null && FolderAdminRules.RoleHolder(ctx.Value.Folders, role, id) is { } holder)
        {
            update[holder.Id] = new JsonObject { ["role"] = null };
        }

        update[id] = new JsonObject { ["role"] = role };
        return await WriteAsync(accountKey, new JsonObject { ["update"] = update }, ct).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> MarkTreeReadCoreAsync(string accountKey, string id, CancellationToken ct)
    {
        var ctx = await ContextAsync(accountKey, ct).ConfigureAwait(false);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        var (client, folders) = ctx.Value;
        var tree = new List<string> { id };
        for (var i = 0; i < tree.Count; i++)
        {
            tree.AddRange(folders.Where(f => string.Equals(f.ParentId, tree[i], StringComparison.Ordinal)).Select(f => f.Id));
        }

        var account = client.Session.MailAccountId;
        var inTree = new JsonObject { ["operator"] = "OR", ["conditions"] = new JsonArray([.. tree.Select(t => (JsonNode?)new JsonObject { ["inMailbox"] = t })]) };
        var filter = new JsonObject { ["operator"] = "AND", ["conditions"] = new JsonArray(inTree, new JsonObject { ["notKeyword"] = "$seen" }) };
        var unread = new List<string>();
        while (true)
        {
            var q = await ReaderCalls.CallAsync(client, "Email/query", new JsonObject { ["accountId"] = account, ["filter"] = filter.DeepClone(), ["position"] = unread.Count, ["limit"] = 500 }, ct).ConfigureAwait(false);
            if (!q.IsOk)
            {
                return Result<Unit>.Fail(q.Error!);
            }

            var page = ReaderCalls.Ids(q.Value!);
            unread.AddRange(page);
            if (page.Count < 500)
            {
                break;
            }
        }

        foreach (var chunk in unread.Chunk(Chunk))
        {
            var update = new JsonObject();
            foreach (var email in chunk)
            {
                update[email] = new JsonObject { ["keywords/$seen"] = true };
            }

            var r = await ReaderCalls.CallAsync(client, "Email/set", new JsonObject { ["accountId"] = account, ["update"] = update }, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<Unit>.Fail(r.Error!);
            }

            if (Problem(r.Value!) is { } why)
            {
                return Result<Unit>.Fail(new ValidationError(why));
            }
        }

        Changed?.Invoke(this, new FolderAdminChangedEventArgs(accountKey));
        return Result<Unit>.Ok(default);
    }

    private async Task<Result<FolderAdminPrefs>> PrefsCoreAsync(string accountKey, CancellationToken ct)
    {
        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<FolderAdminPrefs>.Fail(client.Error!);
        }

        var empty = new FolderAdminPrefs(new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        if (!client.Value!.Session.Has(FolderAdminMetadata.Capability))
        {
            return Result<FolderAdminPrefs>.Ok(empty);
        }

        var read = await FolderAdminMetadata.ReadAsync(client.Value, ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Result<FolderAdminPrefs>.Fail(read.Error!);
        }

        var meta = read.Value.Metadata;
        var account = client.Value.Session.MailAccountId + "/";
        var muted = (FolderAdminMetadata.ValueOf(meta, "notificationMutes")?["mailboxes"] as JsonObject)?
            .Where(kv => kv.Key.StartsWith(account, StringComparison.Ordinal) && kv.Value is JsonValue v && v.TryGetValue<bool>(out var on) && on)
            .Select(kv => kv.Key[account.Length..]) ?? [];
        var pinned = (FolderAdminMetadata.ValueOf(meta, "pinnedFolders") as JsonArray)?.Select(x => x.AsString()).OfType<string>() ?? [];
        return Result<FolderAdminPrefs>.Ok(new FolderAdminPrefs(
            Strings(FolderAdminMetadata.ValueOf(meta, "folderIcons")),
            Strings(FolderAdminMetadata.ValueOf(meta, "folderCountModes")),
            new HashSet<string>(pinned, StringComparer.Ordinal),
            new HashSet<string>(muted, StringComparer.Ordinal)));
    }

    private async Task<Result<Unit>> SetPrefCoreAsync(string accountKey, string folderId, FolderAdminPref pref, string? value, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderId);
        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var account = client.Value!.Session.MailAccountId;
        var done = await FolderAdminMetadata.UpdateAsync(client.Value, meta => PrefPatch(meta, account, folderId, pref, value), ct).ConfigureAwait(false);
        if (done.IsOk)
        {
            Changed?.Invoke(this, new FolderAdminChangedEventArgs(accountKey));
        }

        return done;
    }

    private JsonObject PrefPatch(JsonObject meta, string account, string folderId, FolderAdminPref pref, string? value)
    {
        var clear = string.IsNullOrEmpty(value);
        var (name, next) = pref switch
        {
            FolderAdminPref.Icon => ("folderIcons", (JsonNode)MapWith(FolderAdminMetadata.ValueOf(meta, "folderIcons"), folderId, clear ? null : value)),
            FolderAdminPref.CountMode => ("folderCountModes", MapWith(FolderAdminMetadata.ValueOf(meta, "folderCountModes"), folderId, clear ? null : value)),
            FolderAdminPref.Pin => ("pinnedFolders", PinnedWith(FolderAdminMetadata.ValueOf(meta, "pinnedFolders"), folderId, string.Equals(value, "true", StringComparison.Ordinal))),
            _ => ("notificationMutes", MutesWith(FolderAdminMetadata.ValueOf(meta, "notificationMutes"), account + "/" + folderId, clear ? null : string.Equals(value, "true", StringComparison.Ordinal))),
        };
        return new JsonObject { ["metadata/" + FolderAdminMetadata.ValueKey(name)] = FolderAdminMetadata.Entry(name, next, time.GetUtcNow()) };
    }
}
