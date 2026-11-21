// Mirrors: Rust ctxmenu.rs mailbox_op :751 (mark read, empty, delete folder, rename), mark_read_query :724, dnd.rs plan_folder_drop :149 (reorder and reparent); Bulwark
// lib/jmap/client.ts createMailbox :4636, updateMailbox(es) :4686/:4702, deleteMailbox :4729, emptyMailbox :4389, markMailboxAsRead :4474; Stalwart crates/jmap/src/mailbox/set.rs
// (mailboxHasEmail, mailboxHasChild, myRights). Hooks onBeforeMailboxCreate / onMailboxCreate, ...Rename, ...Delete, ...Empty. Owner actions.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.DragDrop;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Presentation.Actions;

public sealed partial class MailActionService
{
    /// <summary>Creates a folder (top level when <paramref name="parentId"/> is null). The sidebar learns of it through the next Mailbox state change.</summary>
    public async Task<Result<Mailbox>> CreateFolderAsync(string accountKey, string name, string? parentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return Result<Mailbox>.Fail(Failed(new ValidationError("Give the folder a name.")));
        }

        var writer = WriterFor(accountKey);
        if (!writer.IsOk)
        {
            return Result<Mailbox>.Fail(writer.Error!);
        }

        if (!await AllowAsync(HookNames.OnBeforeMailboxCreate, () => new MailboxCreatePayload(trimmed, parentId), ct).ConfigureAwait(true))
        {
            return Result<Mailbox>.Fail(new ValidationError("Stopped by a plugin."));
        }

        var created = await writer.Value!.CreateMailboxAsync(trimmed, parentId, null, ct).ConfigureAwait(true);
        if (!created.IsOk)
        {
            return Result<Mailbox>.Fail(Failed(created.Error!));
        }

        hooks.Fire(HookNames.OnMailboxCreate, () => new MailboxCreatedPayload(created.Value!.Id, trimmed, parentId));
        Notify("Folder created");
        return created;
    }

    /// <summary>Renames a folder.</summary>
    public async Task<Result<Unit>> RenameFolderAsync(string accountKey, string mailboxId, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        var previous = FolderName(mailboxId);
        if (trimmed.Length == 0 || string.Equals(trimmed, previous, StringComparison.Ordinal))
        {
            return Result<Unit>.Ok(default);
        }

        return await UpdateFolderAsync(accountKey, mailboxId, new MailboxPatch { Name = trimmed }, HookNames.OnBeforeMailboxRename, () => new MailboxRenamePayload(mailboxId, trimmed),
            HookNames.OnMailboxRename, () => new MailboxRenamedPayload(mailboxId, trimmed, previous), "Folder renamed", ct).ConfigureAwait(true);
    }

    /// <summary>Deletes a folder after asking; the server refuses a folder with subfolders or messages, and says so.</summary>
    public async Task<Result<Unit>> DeleteFolderAsync(string accountKey, string mailboxId, CancellationToken ct)
    {
        var name = FolderName(mailboxId);
        var writer = WriterFor(accountKey);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        if (!await confirmer.ConfirmAsync(new ConfirmRequest($"Delete folder \"{name}\"?", "The folder is removed. A folder with messages or subfolders is refused.", "Delete"), ct).ConfigureAwait(true)
            || !await AllowAsync(HookNames.OnBeforeMailboxDelete, () => new MailboxDeletePayload(mailboxId, name), ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        var r = await writer.Value!.DestroyMailboxAsync(mailboxId, false, ct).ConfigureAwait(true);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(Failed(r.Error!));
        }

        hooks.Fire(HookNames.OnMailboxDelete, () => new MailboxDeletePayload(mailboxId, name));
        Notify("Folder deleted");
        return r;
    }

    /// <summary>Empties Trash or Spam (or another folder) after asking; the open list reloads in place once the server is done.</summary>
    public async Task<Result<Unit>> EmptyFolderAsync(MailboxRef folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var writer = WriterFor(folder.AccountKey);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        var name = FolderName(folder.MailboxId);
        var ask = new ConfirmRequest($"Empty {name}?", $"Every message in {name} is deleted permanently.", "Empty");
        if (!await confirmer.ConfirmAsync(ask, ct).ConfigureAwait(true)
            || !await AllowAsync(HookNames.OnBeforeMailboxEmpty, () => new MailboxEmptyPayload(folder.MailboxId, folder.Role), ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        var count = await writer.Value!.EmptyMailboxAsync(folder.MailboxId, ct).ConfigureAwait(true);
        if (!count.IsOk)
        {
            return Result<Unit>.Fail(Failed(count.Error!));
        }

        hooks.Fire(HookNames.OnMailboxEmpty, () => new MailboxEmptiedPayload(folder.MailboxId, folder.Role, count.Value));
        if (string.Equals(list.MailboxId, folder.MailboxId, StringComparison.Ordinal))
        {
            await list.ReloadAsync().ConfigureAwait(true);
        }

        Notify($"{Count(count.Value, "1 message", "messages")} deleted");
        return Result<Unit>.Ok(default);
    }

    /// <summary>Marks every message of a folder (and its subfolders on request) read.</summary>
    public async Task<Result<Unit>> MarkFolderReadAsync(MailboxRef folder, bool withChildren, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var writer = WriterFor(folder.AccountKey);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        var count = await writer.Value!.MarkMailboxReadAsync(folder.MailboxId, withChildren, ct).ConfigureAwait(true);
        if (!count.IsOk)
        {
            return Result<Unit>.Fail(Failed(count.Error!));
        }

        if (string.Equals(list.MailboxId, folder.MailboxId, StringComparison.Ordinal))
        {
            await list.ReloadAsync().ConfigureAwait(true);
        }

        Notify(count.Value == 0 ? "Nothing to mark as read" : $"{Count(count.Value, "1 message", "messages")} marked as read");
        return Result<Unit>.Ok(default);
    }

    /// <summary>Marks every unread message of the account read, after asking (Bulwark mail-app.tsx:4756-4778, email-store.ts markAllMailboxesAsRead; the folders with unread messages one by one).</summary>
    public async Task<Result<Unit>> MarkAllFoldersReadAsync(string accountKey, CancellationToken ct)
    {
        var writer = WriterFor(accountKey);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        if (!await confirmer.ConfirmAsync(new ConfirmRequest("Mark all folders as read", "Mark every unread message in your personal account as read?", "Mark all folders as read", Danger: false), ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        var total = 0;
        foreach (var folder in folders.Tree.All.Where(m => m.UnreadEmails > 0))
        {
            var count = await writer.Value!.MarkMailboxReadAsync(folder.Id, false, ct).ConfigureAwait(true);
            if (!count.IsOk)
            {
                return Result<Unit>.Fail(Failed(count.Error!));
            }

            total += count.Value;
        }

        if (list.MailboxId is not null)
        {
            await list.ReloadAsync().ConfigureAwait(true);
        }

        Notify(total == 0 ? "No unread messages" : $"Marked {Count(total, "1 message", "messages")} as read");
        return Result<Unit>.Ok(default);
    }

    /// <summary>Drops a folder on another: reorder next to it or nest inside it (<see cref="FolderDrop.Plan"/>); cycles and role folders are refused with a message.</summary>
    public async Task<Result<Unit>> MoveFolderAsync(string accountKey, string drag, string target, DropZone zone, CancellationToken ct)
    {
        var rows = folders.Tree.All.Select(ToJson).ToList();
        var plan = FolderDrop.Plan(rows, drag, target, zone);
        if (!plan.IsOk)
        {
            return Result<Unit>.Fail(Failed(plan.Error!));
        }

        if (plan.Value!.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var writer = WriterFor(accountKey);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        var patches = plan.Value.ToDictionary(p => p.Id, p => ToPatch(p.Patch), StringComparer.Ordinal);
        var r = await writer.Value!.UpdateMailboxesAsync(patches, ct).ConfigureAwait(true);
        var refusal = r.IsOk ? FirstRefusal(r.Value!) : null;
        if (!r.IsOk || refusal is not null)
        {
            return Result<Unit>.Fail(Failed(r.Error ?? refusal!));
        }

        return Result<Unit>.Ok(default);
    }

    private async Task<Result<Unit>> UpdateFolderAsync<TBefore, TAfter>(
        string accountKey,
        string mailboxId,
        MailboxPatch patch,
        string interceptHook,
        Func<TBefore> before,
        string observerHook,
        Func<TAfter> after,
        string toast,
        CancellationToken ct)
        where TBefore : notnull
        where TAfter : notnull
    {
        var writer = WriterFor(accountKey);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        if (!await AllowAsync(interceptHook, before, ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        var r = await writer.Value!.UpdateMailboxesAsync(new Dictionary<string, MailboxPatch>(StringComparer.Ordinal) { [mailboxId] = patch }, ct).ConfigureAwait(true);
        var refusal = r.IsOk ? FirstRefusal(r.Value!) : null;
        if (!r.IsOk || refusal is not null)
        {
            return Result<Unit>.Fail(Failed(r.Error ?? refusal!));
        }

        hooks.Fire(observerHook, after);
        Notify(toast);
        return Result<Unit>.Ok(default);
    }

    private static JsonObject ToJson(Mailbox m) => new()
    {
        ["id"] = m.Id,
        ["name"] = m.Name,
        ["parentId"] = m.ParentId,
        ["role"] = Core.Jmap.Json.MailboxRoleConverter.Name(m.Role),
        ["sortOrder"] = m.SortOrder,
    };

    private static MailboxPatch ToPatch(JsonObject p) => new()
    {
        SetParent = p.ContainsKey("parentId"),
        ParentId = p["parentId"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null,
        SortOrder = p["sortOrder"] is JsonValue n && n.TryGetValue<int>(out var i) ? i : null,
    };
}
