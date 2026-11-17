// Mirrors: Rust mail.rs (list/body/thread fetches), Bulwark lib/jmap/client.ts (getMailboxes :2635, getEmails :3085,
// getEmail :3517, searchEmails :4746, getThread :5073, getIdentities :5177, getQuota :2518); M1-plan §4.1.
// Contract stub from F2; owner jmap.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail;

/// <summary>The read side of mail for one account.</summary>
public interface IMailService
{
    /// <summary>All mailboxes as a tree.</summary>
    Task<Result<MailboxTree>> GetMailboxesAsync(CancellationToken ct);

    /// <summary>One page of a list (anchor paging).</summary>
    Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct);

    /// <summary>A message with its body values.</summary>
    Task<Result<EmailBody>> GetBodyAsync(string emailId, BodyFetchOptions options, CancellationToken ct);

    /// <summary>The conversation a message is in.</summary>
    Task<Result<ConversationThread>> GetThreadAsync(string threadId, CancellationToken ct);

    /// <summary>The account's sending identities.</summary>
    Task<Result<IReadOnlyList<Identity>>> GetIdentitiesAsync(CancellationToken ct);

    /// <summary>The storage quota, or null when the server reports none.</summary>
    Task<Result<QuotaInfo?>> GetQuotaAsync(CancellationToken ct);
}
