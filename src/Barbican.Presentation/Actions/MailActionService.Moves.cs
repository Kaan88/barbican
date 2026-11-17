// Mirrors: Rust mail.rs apply_confirmed :978 ("delete" permanent in Trash or Junk with permanentlyDeleteJunk, deleteAction trash | trash-and-read | permanent
// :1029, "archive" :1060, "spam" / "notspam" :1054, "move:" :1056), confirm_action :940; Bulwark lib/jmap/client.ts batchDeleteEmails :4090, batchMoveEmails :4134,
// batchArchiveEmails :4162, removeEmailsFromMailbox :4360, restoreEmails :4375, markAsSpam :4577, undoSpam :4616, lib/trash-restore-target.ts, lib/email-action-toast.ts.
// Hooks onBeforeEmailMove / onAfterEmailMove, ...Delete, ...Archive, onEmailUnarchive, ...SpamToggle. Owner actions.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Presentation.Actions;

public sealed partial class MailActionService
{
    /// <summary>Moves messages into one folder (Move to…, a drop on a folder); messages already only there are left out.</summary>
    public Task<Result<Unit>> MoveAsync(IReadOnlyList<MailTarget> targets, string toMailboxId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrEmpty(toMailboxId);
        var wanted = targets.Where(t => !(t.MailboxIds.Count == 1 && t.MailboxIds.ContainsKey(toMailboxId))).ToList();
        if (wanted.Count > 0 && !ActionCommandSet.HasRight(folders, toMailboxId, "mayAddItems"))
        {
            // J-048: a folder whose myRights say mayAddItems=false takes no drop and no move (RFC 8621 2); the server would refuse it anyway
            return Task.FromResult(Result<Unit>.Fail(Failed(new ValidationError("You can't add messages to that folder."))));
        }

        var from = list.MailboxId;
        var name = FolderName(toMailboxId);
        return RemoveFlowAsync(wanted, (_, _) => new RemovalOp(
            "Moved to " + name,
            n => $"{Count(n, "Message", "messages")} moved to {name}",
            true,
            (w, g, c) => w.MoveAsync(Ids(g), null, toMailboxId, c),
            (ids, c) => AllowAsync(HookNames.OnBeforeEmailMove, () => new EmailMovePayload(ids, from, toMailboxId), c),
            ids => hooks.Fire(HookNames.OnAfterEmailMove, () => new EmailMovePayload(ids, from, toMailboxId))), ct);
    }

    /// <summary>Takes messages out of the open folder only (labels mode); they stay in their other folders.</summary>
    public Task<Result<Unit>> RemoveFromFolderAsync(IReadOnlyList<MailTarget> targets, string mailboxId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(mailboxId);
        return RemoveFlowAsync(targets, (_, _) => new RemovalOp(
            "Removed from folder",
            n => $"{Count(n, "Message", "messages")} removed from {FolderName(mailboxId)}",
            true,
            (w, g, c) => w.RemoveFromMailboxAsync(Ids(g), mailboxId, c),
            (ids, c) => AllowAsync(HookNames.OnBeforeEmailMove, () => new EmailMovePayload(ids, mailboxId, null), c),
            ids => hooks.Fire(HookNames.OnAfterEmailMove, () => new EmailMovePayload(ids, mailboxId, null))), ct);
    }

    /// <summary>Move to Inbox (the hover strip and menu in Archive).</summary>
    public async Task<Result<Unit>> MoveToInboxAsync(IReadOnlyList<MailTarget> targets, CancellationToken ct)
    {
        var inbox = RoleFolder(MailboxRole.Inbox);
        if (inbox is null)
        {
            return Result<Unit>.Fail(Failed(new ValidationError("There is no Inbox folder.")));
        }

        var wasArchive = list.Role == MailboxRole.Archive;
        var ids = Ids(targets);
        var r = await MoveAsync(targets, inbox.Id, ct).ConfigureAwait(true);
        if (r.IsOk && wasArchive && ids.Length > 0)
        {
            hooks.Fire(HookNames.OnEmailUnarchive, () => new EmailUnarchivePayload(ids, inbox.Id));
        }

        return r;
    }

    /// <summary>Archives by the archiveMode setting: one Archive folder, or a folder per year or month under it (created when missing, J-045).</summary>
    public async Task<Result<Unit>> ArchiveAsync(IReadOnlyList<MailTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var archive = RoleFolder(MailboxRole.Archive);
        if (archive is null)
        {
            return Result<Unit>.Fail(Failed(new ValidationError("There is no Archive folder.")));
        }

        var settings = Settings;
        if (targets.Count > 0 && settings.ConfirmArchive && !await confirmer.ConfirmAsync(
                new ConfirmRequest(Count(targets.Count, "Archive this message?", "Archive " + targets.Count + " messages?"), "They move to the Archive folder.", "Archive", Danger: false), ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        return await RemoveFlowAsync(targets, (_, _) => new RemovalOp(
            "Archived",
            n => n == 1 ? "Archived" : $"{n} messages archived",
            true,
            (w, g, c) => ArchiveWriteAsync(w, g, archive.Id, settings.ArchiveMode, c),
            (ids, c) => AllowAsync(HookNames.OnBeforeEmailArchive, () => new EmailIdsPayload(ids), c),
            ids => hooks.Fire(HookNames.OnEmailArchive, () => new EmailIdsPayload(ids))), ct).ConfigureAwait(true);
    }

    /// <summary>Marks messages as spam (moves them to Junk and sets <c>$junk</c>) or not spam (back to the Inbox, <c>$notjunk</c>).</summary>
    public Task<Result<Unit>> ReportSpamAsync(IReadOnlyList<MailTarget> targets, bool spam, CancellationToken ct)
    {
        var dest = RoleFolder(spam ? MailboxRole.Junk : MailboxRole.Inbox);
        if (dest is null)
        {
            return Task.FromResult(Result<Unit>.Fail(Failed(new ValidationError(spam ? "There is no Junk folder." : "There is no Inbox folder."))));
        }

        var flags = new Dictionary<string, bool>(StringComparer.Ordinal) { ["$junk"] = spam, ["$notjunk"] = !spam };
        var back = new Dictionary<string, bool>(StringComparer.Ordinal) { ["$junk"] = !spam, ["$notjunk"] = spam };
        return RemoveFlowAsync(targets, (_, _) => new RemovalOp(
            spam ? "Marked as spam" : "Marked as not spam",
            n => spam ? $"{Count(n, "Message", "messages")} marked as spam" : $"{Count(n, "Message", "messages")} moved to Inbox",
            true,
            (w, g, c) => w.MoveWithKeywordsAsync(Ids(g), dest.Id, flags, c),
            (ids, c) => AllowAsync(HookNames.OnBeforeEmailSpamToggle, () => new EmailSpamPayload(ids, spam), c),
            ids => hooks.Fire(HookNames.OnEmailSpamToggle, () => new EmailSpamPayload(ids, spam))) { KeywordsBack = back }, ct);
    }

    /// <summary>
    /// Delete: to Trash (also marking read with deleteAction trash-and-read); in Trash, in Junk with permanentlyDeleteJunk, or with deleteAction permanent
    /// it destroys the messages. Asks first when several messages go and confirmBulkDelete is on, or always with confirmDelete.
    /// </summary>
    public async Task<Result<Unit>> DeleteAsync(IReadOnlyList<MailTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var settings = Settings;
        var permanent = targets.Where(t => IsPermanent(t, settings)).ToList();
        var soft = targets.Except(permanent).ToList();
        if (!await ConfirmDeleteAsync(targets.Count, settings, ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        var first = soft.Count > 0 ? await SoftDeleteAsync(soft, settings, ct).ConfigureAwait(true) : Result<Unit>.Ok(default);
        var second = permanent.Count > 0 ? await DestroyFlowAsync(permanent, ct).ConfigureAwait(true) : Result<Unit>.Ok(default);
        return first.IsOk ? second : first;
    }

    /// <summary>Delete forever: destroys the messages (Trash's "Delete permanently", the menu item).</summary>
    public async Task<Result<Unit>> DeleteForeverAsync(IReadOnlyList<MailTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return await ConfirmDeleteAsync(targets.Count, Settings, ct).ConfigureAwait(true)
            ? await DestroyFlowAsync(targets, ct).ConfigureAwait(true)
            : Result<Unit>.Ok(default);
    }

    /// <summary>Restores messages from Trash by what they are: a draft to Drafts, mail from your own address to Sent, the rest to the Inbox (trash-restore-target.ts).</summary>
    public async Task<Result<Unit>> RestoreAsync(IReadOnlyList<MailTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var own = await OwnAddressesAsync(ct).ConfigureAwait(true);
        var outcome = Result<Unit>.Ok(default);
        foreach (var group in targets.GroupBy(t => RestoreRole(t, own)))
        {
            var dest = RoleFolder(group.Key) ?? RoleFolder(MailboxRole.Inbox);
            if (dest is null)
            {
                outcome = Result<Unit>.Fail(Failed(new ValidationError("There is no folder to restore to.")));
                continue;
            }

            var r = await MoveAsync([.. group], dest.Id, ct).ConfigureAwait(true);
            if (!r.IsOk && outcome.IsOk)
            {
                outcome = r;
            }
        }

        return outcome;
    }

    private bool IsPermanent(MailTarget t, ActionSettings s) =>
        string.Equals(s.DeleteAction, "permanent", StringComparison.Ordinal)
        || InRole(t, MailboxRole.Trash)
        || (s.PermanentlyDeleteJunk && InRole(t, MailboxRole.Junk));

    private bool InRole(MailTarget t, MailboxRole role) =>
        folders.Tree.All.Any(m => m.Role == role && t.MailboxIds.TryGetValue(m.Id, out var on) && on);

    private async Task<bool> ConfirmDeleteAsync(int n, ActionSettings s, CancellationToken ct)
    {
        if (n == 0 || !(s.ConfirmDelete || (n > 1 && s.ConfirmBulkDelete)))
        {
            return true;
        }

        var what = n == 1 ? "this message" : $"{n} messages";
        return await confirmer.ConfirmAsync(new ConfirmRequest(n == 1 ? "Delete this message?" : $"Delete {n} messages?", $"Delete {what}?", "Delete"), ct).ConfigureAwait(true);
    }

    private async Task<Result<Unit>> SoftDeleteAsync(IReadOnlyList<MailTarget> soft, ActionSettings s, CancellationToken ct)
    {
        var trash = RoleFolder(MailboxRole.Trash);
        if (trash is null)
        {
            return Result<Unit>.Fail(Failed(new ValidationError("There is no Trash folder.")));
        }

        var read = new Dictionary<string, bool>(StringComparer.Ordinal) { [Seen] = true };
        var alsoRead = string.Equals(s.DeleteAction, "trash-and-read", StringComparison.Ordinal);
        var from = list.MailboxId;
        return await RemoveFlowAsync(soft, (_, _) => new RemovalOp(
            "Deleted",
            n => n == 1 ? "Message deleted" : $"{n} messages deleted",
            true,
            (w, g, c) => alsoRead ? w.MoveWithKeywordsAsync(Ids(g), trash.Id, read, c) : w.MoveAsync(Ids(g), null, trash.Id, c),
            (ids, c) => AllowAsync(HookNames.OnBeforeEmailDelete, () => new EmailDeletePayload(ids, false), c),
            ids => hooks.Fire(HookNames.OnAfterEmailDelete, () => new EmailDeletePayload(ids, false))), ct).ConfigureAwait(true);
    }

    private Task<Result<Unit>> DestroyFlowAsync(IReadOnlyList<MailTarget> targets, CancellationToken ct) =>
        RemoveFlowAsync(targets, (_, _) => new RemovalOp(
            "Deleted permanently",
            n => n == 1 ? "Message deleted permanently" : $"{n} messages deleted permanently",
            false,
            (w, g, c) => w.DestroyAsync(Ids(g), c),
            (ids, c) => AllowAsync(HookNames.OnBeforeEmailDelete, () => new EmailDeletePayload(ids, true), c),
            ids => hooks.Fire(HookNames.OnAfterEmailDelete, () => new EmailDeletePayload(ids, true))), ct);

    private async Task<Result<SetReport>> ArchiveWriteAsync(IMailWriter writer, IReadOnlyList<MailTarget> group, string archiveId, string mode, CancellationToken ct)
    {
        if (string.Equals(mode, "single", StringComparison.Ordinal))
        {
            return await writer.MoveAsync(Ids(group), null, archiveId, ct).ConfigureAwait(true);
        }

        var boxes = folders.Tree.All.ToList();
        var report = SetReport.Empty;
        var byPath = group.GroupBy(t => string.Join('/', Core.Mail.Archive.ArchivePath.Of(list.Find(t.EmailId)?.ReceivedAt ?? time.GetUtcNow(), mode, time.LocalTimeZone)), StringComparer.Ordinal);
        foreach (var g in byPath)
        {
            var dest = await Core.Mail.Archive.ArchiveFolders.EnsureAsync(writer, boxes, archiveId, g.Key.Length == 0 ? [] : g.Key.Split('/'), ct).ConfigureAwait(true);
            if (!dest.IsOk)
            {
                return Result<SetReport>.Fail(dest.Error!);
            }

            var moved = await writer.MoveAsync(Ids(g), null, dest.Value!, ct).ConfigureAwait(true);
            if (!moved.IsOk)
            {
                return moved;
            }

            report = new SetReport([.. report.Updated, .. moved.Value!.Updated], [.. report.Destroyed, .. moved.Value.Destroyed], [.. report.Created, .. moved.Value.Created], [.. report.Refused, .. moved.Value.Refused]);
        }

        return Result<SetReport>.Ok(report);
    }

    private MailboxRole RestoreRole(MailTarget t, IReadOnlySet<string> own)
    {
        if (t.Keywords.IsSet("$draft"))
        {
            return MailboxRole.Drafts;
        }

        var from = (list.Find(t.EmailId)?.From is { Count: > 0 } f ? f[0].Email : null);
        return from is not null && own.Contains(from) ? MailboxRole.Sent : MailboxRole.Inbox;
    }

    private async Task<IReadOnlySet<string>> OwnAddressesAsync(CancellationToken ct)
    {
        var ids = active.Mail is { } mail ? await mail.GetIdentitiesAsync(ct).ConfigureAwait(true) : Result<IReadOnlyList<Identity>>.Ok([]);
        return ids.IsOk ? new HashSet<string>(ids.Value!.Select(i => i.Email ?? string.Empty).Where(e => e.Length > 0), StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.Ordinal);
    }
}
