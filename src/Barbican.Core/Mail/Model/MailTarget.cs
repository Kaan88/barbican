// Mirrors: M2 plan section 4.1 (MailTarget: one message an action applies to); Bulwark stores/email-store.ts (the selection), lib/jmap/client.ts (the ids a write takes).
// Contract from stage F of M2; owner actions (unified rows carry their source: RowId encodes the login and shared account, M2 plan 4.4 IMailTargetResolver).

namespace Barbican.Core.Mail.Model;

/// <summary>One message an action applies to.</summary>
/// <param name="AccountKey">The account key of the login that holds it.</param>
/// <param name="AccountId">The JMAP account id (a shared account's own id for shared mail).</param>
/// <param name="EmailId">The server email id.</param>
/// <param name="ThreadId">The server thread id.</param>
/// <param name="RowId">The list row id (unified rows encode the source).</param>
/// <param name="MailboxIds">The mailboxes the message is in (id to true).</param>
/// <param name="Keywords">The keywords of the message.</param>
public sealed record MailTarget(
    string AccountKey,
    string AccountId,
    string EmailId,
    string ThreadId,
    string RowId,
    IReadOnlyDictionary<string, bool> MailboxIds,
    Keywords Keywords);
