// Mirrors: Rust mail.rs apply_confirmed :978 (remove_local, write, reload on failure), Bulwark stores/email-store.ts (rollback), lib/email-action-toast.ts (a toast with
// Undo), lib/undo-history.ts (restoreEmails :4375: Undo puts back the folder sets the messages had). Owner actions.
//
// The flow every action that takes rows out of the list shares: intercept, optimistic removal, one write, rollback of the refused rows, observer, undo.

using System.Runtime.CompilerServices;
using Barbican.Core.Common;
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

        var edit = list.RemoveRows(ids);
        var result = await op.Write(writer.Value!, group, ct).ConfigureAwait(true);
        var refused = result.IsOk ? result.Value!.Refused.Select(r => r.Id).ToHashSet(StringComparer.Ordinal) : [];
        var failed = !result.IsOk || refused.Count >= ids.Length;
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
        // Undo, by Ctrl+Z or by the button, closes the toast that offered it (undo-history.ts runUndo closeToast)
        var offered = new StrongBox<ToastHandle?>();
        var entry = op.Undoable
            ? undo.Record(op.Label, undoCt =>
            {
                if (offered.Value is { } shown)
                {
                    toasts.Dismiss(shown);
                }

                return UndoRemovalAsync(account, accepted, edit, op.KeywordsBack, undoCt);
            })
            : null;
        offered.Value = Notify(op.Toast(accepted.Count), entry);
        return Result<Unit>.Ok(default);
    }

    /// <summary>Puts the rows back and writes the folder sets they had (and the keywords the action flipped) back.</summary>
    private async Task<Result<Unit>> UndoRemovalAsync(string account, IReadOnlyList<MailTarget> accepted, ListEdit edit, IReadOnlyDictionary<string, bool>? keywordsBack, CancellationToken ct)
    {
        var writer = WriterFor(account);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        var ids = accepted.Select(t => t.EmailId).ToHashSet(StringComparer.Ordinal);
        list.RestoreRows(new ListEdit([.. edit.Removed.Where(r => ids.Contains(r.Email.Id))], edit.NextId));
        var sets = accepted.ToDictionary(
            t => t.EmailId,
            t => (IReadOnlyDictionary<string, bool>)t.MailboxIds.Where(kv => kv.Value).ToDictionary(kv => kv.Key, _ => true, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var moved = await writer.Value!.SetMailboxIdsAsync(sets, ct).ConfigureAwait(true);
        var refusal = moved.IsOk ? FirstRefusal(moved.Value!) : null;
        if (!moved.IsOk || refusal is not null)
        {
            await list.ReloadAsync().ConfigureAwait(true);
            return Result<Unit>.Fail(moved.Error ?? refusal!);
        }

        if (keywordsBack is not null)
        {
            var kw = await writer.Value!.UpdateKeywordsAsync([.. ids], keywordsBack, ct).ConfigureAwait(true);
            if (!kw.IsOk)
            {
                return Result<Unit>.Fail(kw.Error!);
            }
        }

        Notify("Undone");
        return Result<Unit>.Ok(default);
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
    }
}
