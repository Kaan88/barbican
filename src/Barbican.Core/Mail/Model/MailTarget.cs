// Mirrors: M2 plan section 4.1 (MailTarget: one message an action applies to); Bulwark stores/email-store.ts (the selection), lib/jmap/client.ts (the ids a write takes).
// Contract from stage F of M2; owner actions (unified rows carry their source: RowId encodes the login and shared account, M2 plan 4.4 IMailTargetResolver).
//
// The contract (Fable review 3 R3-02): actions and writers take EmailId as the list knows it, so a unified row's EmailId is its row id and
// AccountKey the account on screen (whose writer routes the row, UnifiedMailWriter). Code that talks to a server itself (downloads, View
// source, Email/parse, TNEF, rules) never uses EmailId or AccountId as they are: it asks Login for the client, JmapAccountId for the account
// and ServerId for the id. AccountId empty means "the login's own mail account".

namespace Barbican.Core.Mail.Model;

/// <summary>One message an action applies to.</summary>
/// <param name="AccountKey">The account key of the login whose writer takes the action (the account on screen for a unified row).</param>
/// <param name="AccountId">The JMAP account id (a shared account's own id for shared mail); empty for the login's own mail account.</param>
/// <param name="EmailId">The email id as the list has it: the server id, or a unified row id (see <see cref="ServerId"/>).</param>
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
    Keywords Keywords)
{
    /// <summary>The source a unified row id names (login, shared account, server id), or null for a plain id.</summary>
    public Unified.UnifiedRowId? Source => Unified.UnifiedRows.ParseRow(EmailId) ?? Unified.UnifiedRows.ParseRow(RowId);

    /// <summary>The account key of the login that holds the message (a unified row's own login).</summary>
    public string Login => Source?.Login ?? AccountKey;

    /// <summary>The server id of the message (a unified row id without its source).</summary>
    public string ServerId => Unified.UnifiedRows.ParseRow(EmailId)?.Id ?? EmailId;

    /// <summary>The JMAP account that holds the message, given the mail account id of <see cref="Login"/>'s session.</summary>
    public string JmapAccountId(string loginMailAccountId)
    {
        ArgumentNullException.ThrowIfNull(loginMailAccountId);
        if (Source is { } row)
        {
            return row.SharedAccount ?? loginMailAccountId;
        }

        return AccountId.Length > 0 ? AccountId : loginMailAccountId;
    }
}
