// Mirrors: Bulwark lib/account-inbox-unread.ts:16 inboxUnread (the account's own inbox; undefined while the folders are not known, distinct
// from a known zero), :30 totalInboxUnread (accounts still loading add nothing). Shared folders are not fetched by MailService (primary mail
// account only), so the inbox found is always the account's own.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Accounts;

/// <summary>Unread counts for the account popover.</summary>
public static class InboxUnread
{
    /// <summary>Unread in the inbox of <paramref name="tree"/>, or null when unknown.</summary>
    public static int? Of(MailboxTree? tree) =>
        tree?.All.FirstOrDefault(m => m.Role == MailboxRole.Inbox) is { } inbox ? inbox.UnreadEmails : null;

    /// <summary>The sum over the given rows; unknown counts add nothing.</summary>
    public static int Total(IEnumerable<AccountStatus> rows) => rows.Sum(r => r.InboxUnread ?? 0);
}
