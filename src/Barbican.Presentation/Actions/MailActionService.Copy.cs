// Mirrors: Bulwark stores/email-store.ts:5728-5773 copyEmailsToAccount (the messages' own login groups, a copy into the account they are in is
// refused, keepOriginal), hooks/use-message-file-actions.ts:181-195 (the count in the toast, "Copy failed" as the error), locales/en/common.json
// notifications.emails_copied. No move hooks: a copy leaves the messages where they are (email-store.ts:5571-5579). G-048. Owner actions.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Actions;

public sealed partial class MailActionService
{
    /// <summary>The other signed-in accounts and the folders a copy may land in.</summary>
    public Task<Result<IReadOnlyList<CopyTargetAccount>>> CopyTargetsAsync(string? sourceAccountKey, CancellationToken ct) =>
        transfer.TargetsAsync(sourceAccountKey, ct);

    /// <summary>
    /// Copies the messages into a folder of another signed-in account and leaves the originals (and the list) as they are; says how many arrived.
    /// A selection whose messages already live in the destination account is refused, as Bulwark refuses it.
    /// </summary>
    public async Task<Result<Unit>> CopyToAccountAsync(
        IReadOnlyList<MailTarget> targets, string destAccountKey, string destMailboxId, string destFolderName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrEmpty(destAccountKey);
        ArgumentException.ThrowIfNullOrEmpty(destMailboxId);
        if (targets.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        if (targets.Any(t => string.Equals(t.Login, destAccountKey, StringComparison.Ordinal)))
        {
            return Result<Unit>.Fail(Failed(new ValidationError("The messages are already in that account")));
        }

        var r = await transfer.CopyAsync(targets, destAccountKey, destMailboxId, ct).ConfigureAwait(true);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(Failed(r.Error!));
        }

        Notify($"{Count(r.Value, "Message", "messages")} copied to {destFolderName}");
        return Result<Unit>.Ok(default);
    }
}
