// Mirrors: Rust unified.rs:38 row_id, :43 parse_row, :50 real_id, :55 same_source, :139 sum_other_logins, :226 source_key, :231 shared_accounts, :275 dedupe_by,
// :326 tag_row, :342 merge_sorted, :349 positions, :456 match_mailbox, :463 moveto_action; Bulwark stores/email-store.ts (unified inbox, __unified),
// lib/shared-accounts.ts; RFC 8620 section 2 (session accounts: isPersonal, accountCapabilities). Stub from stage F of M2; owner unified (wave 2).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Unified;

/// <summary>The pure bookkeeping of the unified inbox and shared accounts.</summary>
public static class UnifiedRows
{
    /// <summary>The sum of the other logins' inbox unread counts (negative counts as 0), leaving out the active login.</summary>
    public static long SumOtherLogins(IReadOnlyDictionary<string, long> countsByLogin, string active)
    {
        throw new PortPendingException("unified.rs:139 sum_other_logins");
    }

    /// <summary>Orders a merged list newest first by the parsed receivedAt (offsets differ between servers) and drops repeats of the same source and id.</summary>
    public static void MergeSorted(IList<JsonObject> list)
    {
        throw new PortPendingException("unified.rs:342 merge_sorted");
    }

    /// <summary>The list id of a unified row: login, shared account (empty for the login's own) and server id, with separators that cannot occur in ids.</summary>
    public static string RowId(string login, string? sharedAccount, string id)
    {
        throw new PortPendingException("unified.rs:38 row_id");
    }

    /// <summary>The parts of a unified row id, or null for a plain id.</summary>
    public static UnifiedRowId? ParseRow(string rowId)
    {
        throw new PortPendingException("unified.rs:43 parse_row");
    }

    /// <summary>The server id of a list id (unified rows carry their source in front).</summary>
    public static string RealId(string rowId)
    {
        throw new PortPendingException("unified.rs:50 real_id");
    }

    /// <summary>Another message of the same source as the row <paramref name="like"/> (thread members).</summary>
    public static string SameSource(string like, string id)
    {
        throw new PortPendingException("unified.rs:55 same_source");
    }

    /// <summary>Gives a fetched message its unified row id (the server id stays in __id), thread key (__tkey), __account, __src and __jmap.</summary>
    public static void TagRow(JsonObject message, string login, string? sharedAccount, string sourceKey)
    {
        throw new PortPendingException("unified.rs:326 tag_row");
    }

    /// <summary>Reads each account once: an own account beats the same account shared through another login; the same id on another server stays.</summary>
    public static IReadOnlyList<T> DedupeBy<T, TKey>(IEnumerable<T> items, Func<T, bool> shared, Func<T, TKey> key)
        where TKey : notnull
    {
        throw new PortPendingException("unified.rs:275 dedupe_by");
    }

    /// <summary>Per-source counts of loaded rows (__src): the position of each source's next page.</summary>
    public static IReadOnlyDictionary<string, int> Positions(IReadOnlyList<JsonObject> rows)
    {
        throw new PortPendingException("unified.rs:349 positions");
    }

    /// <summary>The shared mail accounts a login can reach: session accounts other than its own, not personal, with the mail capability; sorted.</summary>
    public static IReadOnlyList<string> SharedAccounts(JsonNode session, string ownAccountId)
    {
        throw new PortPendingException("unified.rs:231 shared_accounts");
    }

    /// <summary>The key used for paging and counts: the login, plus "#account" for a shared account.</summary>
    public static string SourceKey(string login, string? sharedAccount)
    {
        throw new PortPendingException("unified.rs:226 source_key");
    }

    /// <summary>The destination in another account for the folder the user picked: the same role, else the same name (case-insensitive).</summary>
    public static string? MatchMailbox(IReadOnlyList<JsonNode> mailboxes, string role, string name)
    {
        throw new PortPendingException("unified.rs:456 match_mailbox");
    }

    /// <summary>Encodes a "move to the matching folder" action: "moveto:" + role + U+001F + name.</summary>
    public static string MoveToAction(string role, string name)
    {
        throw new PortPendingException("unified.rs:463 moveto_action");
    }
}
