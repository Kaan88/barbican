// Mirrors: Bulwark hooks/use-message-file-actions.ts:156-172 (every other connected client's folders are loaded once) and
// stores/email-store.ts:5728-5773 copyEmailsToAccount over lib/jmap/client.ts:12242/:12290; G-048. Owner actions.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;

namespace Barbican.Presentation.Actions;

/// <summary>The picker and the copy of the signed-in accounts, over their sessions.</summary>
public sealed class SessionMailTransfer(IAccountManager accounts, IMailWriterFactory writers) : IMailTransfer
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CopyTargetAccount>>> TargetsAsync(string? sourceAccountKey, CancellationToken ct)
    {
        var rows = new List<(string AccountKey, string Label, MailboxTree Tree)>();
        foreach (var account in accounts.Accounts)
        {
            // Bulwark leaves the message's own login out; a read-only account could not take the copy anyway.
            if (string.Equals(account.Key, sourceAccountKey, StringComparison.Ordinal)
                || accounts.SessionFor(account.Key) is not { Client.IsReadOnly: false } session)
            {
                continue;
            }

            var tree = await session.Mail.GetMailboxesAsync(ct).ConfigureAwait(false);
            if (tree.IsOk && tree.Value is { } loaded)
            {
                rows.Add((account.Key, account.User, loaded));
            }
        }

        return Result<IReadOnlyList<CopyTargetAccount>>.Ok(CopyTargetAccounts.Build(rows));
    }

    /// <inheritdoc />
    public Task<Result<int>> CopyAsync(IReadOnlyList<MailTarget> targets, string destAccountKey, string destMailboxId, CancellationToken ct) =>
        new CrossAccountCopy(key => accounts.SessionFor(key) is { } session ? (writers.ForSession(session), session.Client) : null)
            .CopyAsync(targets, destAccountKey, destMailboxId, ct);
}
