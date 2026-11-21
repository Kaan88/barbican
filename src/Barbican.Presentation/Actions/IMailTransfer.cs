// Mirrors: Bulwark stores/email-store.ts:5728-5773 copyEmailsToAccount and :5601-5648 crossAccountMoveEmails (keepOriginal); G-048. Owner actions.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Actions;

/// <summary>Copying mail into another signed-in account: which accounts and folders are offered, and the copy itself.</summary>
public interface IMailTransfer
{
    /// <summary>The other signed-in accounts and the folders a copy may land in; empty when there are none.</summary>
    Task<Result<IReadOnlyList<CopyTargetAccount>>> TargetsAsync(string? sourceAccountKey, CancellationToken ct);

    /// <summary>Copies the targets into one folder of another account, keeping the originals; the count that arrived.</summary>
    Task<Result<int>> CopyAsync(IReadOnlyList<MailTarget> targets, string destAccountKey, string destMailboxId, CancellationToken ct);
}
