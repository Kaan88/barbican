// Mirrors: RFC 8621 section 4.6 (Email/set), 4.7 (Email/copy), 4.8 (Email/import), 2.5 (Mailbox/set); RFC 8620 sections 5.3, 6.1; Bulwark lib/jmap/client.ts batchUpdateKeywords :3859,
// batchDeleteEmails :4090, batchMoveEmails :4134, batchArchiveEmails :4162, removeEmailsFromMailbox :4360, restoreEmails :4375, emptyMailbox :4389, markMailboxAsRead :4474,
// markAsSpam :4577, undoSpam :4616, createMailbox :4636, updateMailbox(es) :4686/:4702, deleteMailbox :4729, uploadBlob :6662, importEmail :6770, copyEmailAcrossAccounts :12278,
// importUploadedEmails :12368; Stalwart crates/jmap/src/{email/{set,copy,import},mailbox/set,blob/upload}.rs. Contract from stage F of M2 (M2 plan 4.3); owner actions.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Write;

/// <summary>The write side of mail for one account; chunks by maxObjectsInSet (J-009), never retries a write, refuses locally on a read-only account.</summary>
public interface IMailWriter
{
    Task<Result<SetReport>> UpdateKeywordsAsync(IReadOnlyList<string> emailIds, IReadOnlyDictionary<string, bool> patch, CancellationToken ct);

    Task<Result<SetReport>> MoveAsync(IReadOnlyList<string> emailIds, string? fromMailboxId, string toMailboxId, CancellationToken ct);

    /// <summary>Moves messages to one folder (the whole folder set is replaced) and changes keywords in the same patch: junk ($junk), trash-and-read ($seen). Bulwark markAsSpam :4577.</summary>
    Task<Result<SetReport>> MoveWithKeywordsAsync(IReadOnlyList<string> emailIds, string toMailboxId, IReadOnlyDictionary<string, bool> keywords, CancellationToken ct);

    /// <summary>Takes messages out of one folder and leaves them in the others (a pointer patch; labels mode). Bulwark removeEmailsFromMailbox :4360.</summary>
    Task<Result<SetReport>> RemoveFromMailboxAsync(IReadOnlyList<string> emailIds, string mailboxId, CancellationToken ct);

    Task<Result<SetReport>> SetMailboxIdsAsync(IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> mailboxIdsById, CancellationToken ct);

    Task<Result<SetReport>> DestroyAsync(IReadOnlyList<string> emailIds, CancellationToken ct);

    Task<Result<int>> EmptyMailboxAsync(string mailboxId, CancellationToken ct);

    Task<Result<int>> MarkMailboxReadAsync(string mailboxId, bool withChildren, CancellationToken ct);

    Task<Result<Mailbox>> CreateMailboxAsync(string name, string? parentId, string? role, CancellationToken ct);

    Task<Result<SetReport>> UpdateMailboxesAsync(IReadOnlyDictionary<string, MailboxPatch> patches, CancellationToken ct);

    Task<Result<Unit>> DestroyMailboxAsync(string mailboxId, bool removeEmails, CancellationToken ct);

    Task<Result<UploadedBlob>> UploadAsync(ReadOnlyMemory<byte> data, string type, CancellationToken ct);

    Task<Result<EmailImportReport>> ImportAsync(IReadOnlyList<EmailImportEntry> entries, CancellationToken ct);

    Task<Result<SetReport>> CopyToAccountAsync(string fromAccountId, IReadOnlyList<EmailCopyEntry> entries, CancellationToken ct);
}
