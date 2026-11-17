// Mirrors: Rust mail.rs apply_confirmed :978 (remove_local, write, reload on failure), Bulwark stores/email-store.ts (rollback), lib/email-action-toast.ts (a toast with
// Undo), lib/undo-history.ts (restoreEmails :4375: Undo puts back the folder sets the messages had). Owner actions.
//
// The flow every action that takes rows out of the list shares: intercept, optimistic removal, one write, rollback of the refused rows, observer, undo.

using System.Runtime.CompilerServices;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;

namespace Barbican.Presentation.Actions;

public sealed partial class MailActionService
{
    private async Task<Result<Unit>> RemoveFlowAsync(IReadOnlyList<MailTarget> targets, Func<string, IReadOnlyList<MailTarget>, RemovalOp> make, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return await PerAccountAsync(targets, (account, group) => RemoveOneAsync(account, group, make(account, group), ct)).ConfigureAwait(true);
    }

    private async Task<Result<Unit>> RemoveOneAsync(string account, IReadOnlyList<MailTarget> group, RemovalOp op, CancellationToken ct)
    {
        if (group.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var writer = WriterFor(account);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        var ids = Ids(group);
        if (!await op.Allow(ids, ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        // the folder on screen when the action ran: Undo falls back to it for a message whose folders were not known (never an empty mailboxIds)
        var source = list.MailboxId is { } open && folders.Tree.All.Any(m => string.Equals(m.Id, open, StringComparison.Ordinal)) ? open : null;
        var edit = list.RemoveRows(ids);
        var result = await WriteOrRestoreAsync(op, writer.Value!, group, edit, ct).ConfigureAwait(true);
        var refused = result.IsOk ? result.Value!.Refused.Select(r => r.Id).ToHashSet(StringComparer.Ordinal) : [];
        // a chunked write that failed part way still says what the earlier chunks did (R3-03): those rows stay gone and get their undo
        var applied = !result.IsOk && result.Value is { } partial ? partial.Updated.Concat(partial.Destroyed).ToHashSet(StringComparer.Ordinal) : [];
        var failed = !result.IsOk || refused.Count >= ids.Length;
        if (!result.IsOk && applied.Count > 0)
        {
            list.RestoreRows(new ListEdit([.. edit.Removed.Where(r => !applied.Contains(r.Email.Id))], edit.NextId));
            var appliedTargets = group.Where(t => applied.Contains(t.EmailId)).ToList();
            Failed(result.Error!);
            op.Observe(Ids(appliedTargets));
            var partialEntry = RecordUndo(account, op, appliedTargets, edit, source);
            partialEntry.Offered.Value = Notify(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{appliedTargets.Count} of {group.Count} messages {op.Verb}"), partialEntry.Entry);
            return Result<Unit>.Fail(result.Error!);
        }

        if (failed || refused.Count > 0)
        {
            // the rows the server did not take come back where they were; the ones it took stay gone
            list.RestoreRows(failed ? edit : new ListEdit([.. edit.Removed.Where(r => refused.Contains(r.Email.Id))], edit.NextId));
            var error = result.Error ?? FirstRefusal(result.Value!)!;
            Failed(error);
            if (failed)
            {
                return Result<Unit>.Fail(error);
            }
        }

        var accepted = group.Where(t => !refused.Contains(t.EmailId)).ToList();
        var acceptedIds = Ids(accepted);
        op.Observe(acceptedIds);
        var recorded = RecordUndo(account, op, accepted, edit, source);
        recorded.Offered.Value = Notify(op.Toast(accepted.Count), recorded.Entry);
        return Result<Unit>.Ok(default);
    }

    /// <summary>Records the undo of the rows the server took; the box takes the toast that offers it, so Undo closes it.</summary>
    private (UndoEntry? Entry, StrongBox<ToastHandle?> Offered) RecordUndo(string account, RemovalOp op, IReadOnlyList<MailTarget> accepted, ListEdit edit, string? source)
    {
        // Undo, by Ctrl+Z or by the button, closes the toast that offered it (undo-history.ts runUndo closeToast)
        var offered = new StrongBox<ToastHandle?>();
        var entry = op.Undoable
            ? undo.Record(op.Label, undoCt =>
            {
                if (offered.Value is { } shown)
                {
                    toasts.Dismiss(shown);
                }

                return UndoRemovalAsync(account, accepted, edit, source, op.KeywordsBack, undoCt);
            })
            : null;
        return (entry, offered);
    }

    /// <summary>The one write; a cancelled write puts the rows back before the cancellation goes on (the server was not asked to finish).</summary>
    private async Task<Result<SetReport>> WriteOrRestoreAsync(RemovalOp op, IMailWriter writer, IReadOnlyList<MailTarget> group, ListEdit edit, CancellationToken ct)
    {
        try
        {
            return await op.Write(writer, group, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            list.RestoreRows(edit);
            throw;
        }
    }

    /// <summary>
    /// Puts the rows back and writes the folder sets they had (and the keywords the action flipped) back (Bulwark lib/jmap/client.ts:4375 restoreEmails).
    /// A message is never sent with an empty mailboxIds: RFC 8621 4.1.1 says an Email is in at least one Mailbox and Stalwart refuses it
    /// (crates/jmap/src/email/set.rs:978 "Message has to belong to at least one mailbox"); a message whose folders were not known goes back to the folder
    /// the action ran in, else to the Inbox. The message that was open opens again (Bulwark components/mail/mail-app.tsx:4083 reopenAfterUndo).
    /// </summary>
    private async Task<Result<Unit>> UndoRemovalAsync(string account, IReadOnlyList<MailTarget> accepted, ListEdit edit, string? source, IReadOnlyDictionary<string, bool>? keywordsBack, CancellationToken ct)
    {
        var writer = WriterFor(account);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        var fallback = source ?? RoleFolder(MailboxRole.Inbox)?.Id;
        var sets = new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal);
        foreach (var t in accepted)
        {
            var set = UndoSet(t.MailboxIds, fallback);
            if (set.Count == 0)
            {
                // nothing to put it back into: leave it where the action put it rather than send an empty set
                continue;
            }

            sets[t.EmailId] = set;
        }

        if (sets.Count == 0)
        {
            return Result<Unit>.Fail(Failed(new ValidationError("There is no folder to put the message back into.")));
        }

        var ids = sets.Keys.ToHashSet(StringComparer.Ordinal);
        list.RestoreRows(new ListEdit([.. edit.Removed.Where(r => ids.Contains(r.Email.Id))], edit.NextId) { View = edit.View });
        var moved = await writer.Value!.SetMailboxIdsAsync(sets, ct).ConfigureAwait(true);
        var refusal = moved.IsOk ? FirstRefusal(moved.Value!) : null;
        if (!moved.IsOk || refusal is not null)
        {
            await list.ReloadAsync().ConfigureAwait(true);
            var error = moved.Error ?? refusal!;
            return Result<Unit>.Fail(error is SetError { Type: "notFound" }
                ? new ValidationError("That message no longer exists on the server, so it can't be put back.")
                : error);
        }

        if (keywordsBack is not null)
        {
            var kw = await writer.Value!.UpdateKeywordsAsync([.. ids], keywordsBack, ct).ConfigureAwait(true);
            if (!kw.IsOk)
            {
                return Result<Unit>.Fail(kw.Error!);
            }
        }

        if (edit.OpenId is { } reopen && ids.Contains(reopen))
        {
            list.Reopen(edit);
        }

        Notify("Undone");
        return Result<Unit>.Ok(default);
    }

    /// <summary>The folder set Undo writes: the folders the message had, else <paramref name="fallback"/>; empty only when neither is known.</summary>
    internal static IReadOnlyDictionary<string, bool> UndoSet(IReadOnlyDictionary<string, bool> had, string? fallback)
    {
        ArgumentNullException.ThrowIfNull(had);
        var set = had.Where(kv => kv.Value && kv.Key.Length > 0).ToDictionary(kv => kv.Key, _ => true, StringComparer.Ordinal);
        if (set.Count == 0 && !string.IsNullOrEmpty(fallback))
        {
            set[fallback] = true;
        }

        return set;
    }

    /// <summary>What one removal action does, so the shared flow can run it.</summary>
    /// <param name="Label">The undo history entry.</param>
    /// <param name="Toast">The toast text for the number of messages that went.</param>
    /// <param name="Undoable">A toast with Undo and a history entry (not for a permanent delete).</param>
    /// <param name="Write">The one write.</param>
    /// <param name="Allow">The intercept hook: false stops the action before anything changes.</param>
    /// <param name="Observe">The observer hook, told which messages the server accepted.</param>
    private sealed record RemovalOp(
        string Label,
        Func<int, string> Toast,
        bool Undoable,
        Func<IMailWriter, IReadOnlyList<MailTarget>, CancellationToken, Task<Result<SetReport>>> Write,
        Func<IReadOnlyList<string>, CancellationToken, ValueTask<bool>> Allow,
        Action<IReadOnlyList<string>> Observe)
    {
        /// <summary>Keywords the action flipped, flipped back by Undo (junk flags).</summary>
        public IReadOnlyDictionary<string, bool>? KeywordsBack { get; init; }

        /// <summary>What happened to the messages, for "N of M moved" when a chunked write stopped part way.</summary>
        public string Verb { get; init; } = "moved";
    }
}
