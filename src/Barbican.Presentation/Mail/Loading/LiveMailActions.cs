// Mirrors: Rust reader.rs mark-as-read on open (markAsReadDelay in the loader), M2 plan 4.3 (the real IMailActions: writes only when the account is writable, ReadOnlyPolicy);
// replaces ReadOnlyMailActions. Owner actions.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The reader's mark-as-read, through the action service (hooks, optimistic row) without a toast or an undo entry.</summary>
public sealed class LiveMailActions(MailActionService actions, IListEditor list, IMailWriterSource writers, IActiveMail active) : IMailActions
{
    /// <inheritdoc />
    public bool CanWrite => active.AccountKey is { } key && writers.CanWrite(key);

    /// <inheritdoc />
    public Task<Result<Unit>> MarkReadAsync(string emailId, CancellationToken ct)
    {
        if (!CanWrite || active.AccountKey is not { } key)
        {
            return Task.FromResult(Result<Unit>.Fail(new ReadOnlyViolation("Email/set") { Message = "This account is read-only." }));
        }

        var email = list.Find(emailId);
        var target = new MailTarget(key, string.Empty, emailId, email?.ThreadId ?? string.Empty, emailId, email?.MailboxIds ?? new Dictionary<string, bool>(StringComparer.Ordinal), email?.Keywords ?? Keywords.None);
        return actions.MarkOpenedReadAsync(target, ct);
    }
}
