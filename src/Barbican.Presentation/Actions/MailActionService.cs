// Mirrors: Rust mail.rs apply :925, apply_confirmed :978 (optimistic local change, then the write, then a reload on failure), patch_local :887, remove_local :901,
// next_after :871; Bulwark stores/email-store.ts (optimistic updates and rollback), lib/jmap/client.ts batchUpdateKeywords :3859, batchDeleteEmails :4090,
// batchMoveEmails :4134, markAsSpam :4577, lib/email-action-toast.ts (a toast with Undo), lib/undo-history.ts. Owner actions.
//
// Every action: refuse a read-only account (no request), ask the intercept hook (false stops it, no request), change the list at once through
// IListEditor (rows leave and return by ListSync: no reset), write once (never retried), and on a failure put the rows back and show the error. On
// success the observer hook fires and an undo entry (and a toast with Undo) is recorded.

using System.Globalization;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Actions;

/// <summary>The mail and folder actions behind every menu, key and button.</summary>
public sealed partial class MailActionService(
    IMailWriterSource writers,
    IMailboxDirectory folders,
    IListEditor list,
    IHookBus hooks,
    IUndoService undo,
    IToastService toasts,
    IPrefsStore prefs,
    IActionConfirmer confirmer,
    IActiveMail active,
    IMailTransfer transfer,
    TimeProvider time)
{
    private ActionSettings Settings => ActionSettings.Read(prefs.Current);

    /// <summary>Runs one action per account when the targets come from several (the unified list); the first failure is returned, the rest still run.</summary>
    private static async Task<Result<Unit>> PerAccountAsync(IReadOnlyList<MailTarget> targets, Func<string, IReadOnlyList<MailTarget>, Task<Result<Unit>>> run)
    {
        Result<Unit> outcome = Result<Unit>.Ok(default);
        foreach (var group in targets.GroupBy(t => t.AccountKey, StringComparer.Ordinal))
        {
            var r = await run(group.Key, [.. group]).ConfigureAwait(true);
            if (!r.IsOk && outcome.IsOk)
            {
                outcome = r;
            }
        }

        return outcome;
    }

    /// <summary>The writer of the account, or a failure (and its toast) when the account is read-only or has no session. No request is built.</summary>
    private Result<IMailWriter> WriterFor(string accountKey)
    {
        var writer = writers.WriterOf(accountKey);
        if (writer is null)
        {
            return Result<IMailWriter>.Fail(Failed(new TransportError("This account is not connected.", Retryable: true)));
        }

        return writers.CanWrite(accountKey)
            ? Result<IMailWriter>.Ok(writer)
            : Result<IMailWriter>.Fail(Failed(new ReadOnlyViolation("Email/set") { Message = "This account is read-only." }));
    }

    private JmapError Failed(JmapError error)
    {
        toasts.Show(new ToastRequest(error.Message, IsError: true));
        return error;
    }

    private ToastHandle Notify(string text, UndoEntry? entry = null) =>
        entry is null
            ? toasts.Show(new ToastRequest(text))
            : toasts.Show(new ToastRequest(text, ActionLabel: "Undo", Action: ct => undo.UndoAsync(entry, ct)));

    private ValueTask<bool> AllowAsync<T>(string hook, Func<T> payload, CancellationToken ct) => hooks.AllowAsync(hook, payload, ct);

    private static string Count(int n, string one, string many) =>
        n == 1 ? one : string.Create(CultureInfo.InvariantCulture, $"{n} {many}");

    private string FolderName(string id) =>
        folders.Tree.All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal))?.Name ?? "folder";

    private Mailbox? RoleFolder(MailboxRole role) => folders.Tree.All.FirstOrDefault(m => m.Role == role);

    private static string[] Ids(IEnumerable<MailTarget> targets) => [.. targets.Select(t => t.EmailId)];

    /// <summary>The first refusal the server gave, as the error to show; null when everything was accepted.</summary>
    private static SetError? FirstRefusal(SetReport report) => report.Refused.Count > 0 ? report.Refused[0] : null;

    private static EmailSummary WithKeywords(EmailSummary e, IReadOnlyDictionary<string, bool> patch)
    {
        var map = new Dictionary<string, bool>(e.Keywords.Map, StringComparer.Ordinal);
        foreach (var (keyword, on) in patch)
        {
            if (on)
            {
                map[keyword] = true;
            }
            else
            {
                map.Remove(keyword);
            }
        }

        return e with { Keywords = new Keywords(map) };
    }
}
