// Mirrors: Rust unified.rs:38 row_id, :43 parse_row, :50 real_id, :55 same_source, :139 sum_other_logins, :226 source_key, :231 shared_accounts, :275 dedupe_by,
// :290 inbox_of, :326 tag_row, :336 ts, :342 merge_sorted, :349 positions, :456 match_mailbox, :463 moveto_action; Bulwark lib/unified-mailbox.ts (unified inbox,
// __unified: dedupeUnifiedAccounts, positionsByAccount, the merge by parsed time), lib/shared-accounts.ts; RFC 8620 section 2 (session accounts: isPersonal,
// accountCapabilities). Owner unified (M2 wave 2).

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Mail.Unified;

/// <summary>The pure bookkeeping of the unified inbox and shared accounts.</summary>
public static class UnifiedRows
{
    /// <summary>The folder id of the unified inbox ("All inboxes"), as Rust FOLDER_ID and Bulwark's __unified.</summary>
    public const string FolderId = "__unified";

    // Several sources share short JMAP ids ("b", "c", ...), so a unified row is keyed by its login, the shared JMAP account it was read from (empty for the
    // login's own) and its id. U+001C and U+001D never occur in a JMAP id (RFC 8620 section 1.2: A-Z a-z 0-9 - _) nor in a login.
    private const char RowAccount = '\u001c';
    private const char RowIdSeparator = '\u001d';

    /// <summary>The sum of the other logins' inbox unread counts (negative counts as 0), leaving out the active login.</summary>
    public static long SumOtherLogins(IReadOnlyDictionary<string, long> countsByLogin, string active)
    {
        ArgumentNullException.ThrowIfNull(countsByLogin);
        long sum = 0;
        foreach (var (login, count) in countsByLogin)
        {
            if (!string.Equals(login, active, StringComparison.Ordinal))
            {
                sum += Math.Max(0, count);
            }
        }

        return sum;
    }

    /// <summary>Orders a merged list newest first by the parsed receivedAt (offsets differ between servers) and drops repeats of the same source and id.</summary>
    public static void MergeSorted(IList<JsonObject> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var seen = new HashSet<(string, string)>();
        var kept = new List<JsonObject>(list.Count);
        foreach (var m in list)
        {
            if (seen.Add((Str(m, "__src") ?? string.Empty, Str(m, "id") ?? string.Empty)))
            {
                kept.Add(m);
            }
        }

        // OrderByDescending is stable, as Rust's sort_by_key: equal times keep their arrival order.
        var sorted = kept.OrderByDescending(Timestamp).ToList();
        list.Clear();
        foreach (var m in sorted)
        {
            list.Add(m);
        }
    }

    /// <summary>The list id of a unified row: login, shared account (empty for the login's own) and server id, with separators that cannot occur in ids.</summary>
    public static string RowId(string login, string? sharedAccount, string id) =>
        string.Concat(login, RowAccount.ToString(), sharedAccount ?? string.Empty, RowIdSeparator.ToString(), id);

    /// <summary>The parts of a unified row id, or null for a plain id.</summary>
    public static UnifiedRowId? ParseRow(string rowId)
    {
        ArgumentNullException.ThrowIfNull(rowId);
        var cut = rowId.LastIndexOf(RowIdSeparator);
        if (cut < 0)
        {
            return null;
        }

        var prefix = rowId[..cut];
        var real = rowId[(cut + 1)..];
        var account = prefix.IndexOf(RowAccount, StringComparison.Ordinal);
        if (account < 0)
        {
            return null;
        }

        var shared = prefix[(account + 1)..];
        return new UnifiedRowId(prefix[..account], shared.Length == 0 ? null : shared, real);
    }

    /// <summary>The server id of a list id (unified rows carry their source in front).</summary>
    public static string RealId(string rowId) => ParseRow(rowId)?.Id ?? rowId;

    /// <summary>Another message of the same source as the row <paramref name="like"/> (thread members).</summary>
    public static string SameSource(string like, string id) =>
        ParseRow(like) is { } r ? RowId(r.Login, r.SharedAccount, id) : id;

    /// <summary>Gives a fetched message its unified row id (the server id stays in __id), thread key (__tkey), __account, __src and __jmap.</summary>
    public static void TagRow(JsonObject message, string login, string? sharedAccount, string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(message);
        var real = Str(message, "id") ?? string.Empty;
        message["__id"] = real;
        message["id"] = RowId(login, sharedAccount, real);
        if (Str(message, "threadId") is { } thread)
        {
            message["__tkey"] = RowId(login, sharedAccount, thread);
        }

        message["__account"] = login;
        message["__src"] = sourceKey;
        if (sharedAccount is not null)
        {
            message["__jmap"] = sharedAccount;
        }
    }

    /// <summary>Reads each account once: an own account beats the same account shared through another login; the same id on another server stays.</summary>
    public static IReadOnlyList<T> DedupeBy<T, TKey>(IEnumerable<T> items, Func<T, bool> shared, Func<T, TKey> key)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(key);
        var all = items.ToList();
        var seen = new HashSet<TKey>(all.Where(s => !shared(s)).Select(key));
        return all.Where(s => !shared(s) || seen.Add(key(s))).ToList();
    }

    /// <summary>Per-source counts of loaded rows (__src): the position of each source's next page.</summary>
    public static IReadOnlyDictionary<string, int> Positions(IReadOnlyList<JsonObject> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var p = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in rows)
        {
            if (Str(m, "__src") is { } k)
            {
                p[k] = p.GetValueOrDefault(k) + 1;
            }
        }

        return p;
    }

    /// <summary>The shared mail accounts a login can reach: session accounts other than its own, not personal, with the mail capability; sorted.</summary>
    public static IReadOnlyList<string> SharedAccounts(JsonNode session, string ownAccountId)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session["accounts"] is not JsonObject accounts)
        {
            return [];
        }

        var output = new List<string>();
        foreach (var (id, account) in accounts)
        {
            if (string.Equals(id, ownAccountId, StringComparison.Ordinal) || account is not JsonObject a)
            {
                continue;
            }

            var personal = a["isPersonal"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
            var mail = a["accountCapabilities"] is JsonObject caps && caps.ContainsKey("urn:ietf:params:jmap:mail");
            if (!personal && mail)
            {
                output.Add(id);
            }
        }

        output.Sort(StringComparer.Ordinal);
        return output;
    }

    /// <summary>The key used for paging and counts: the login, plus "#account" for a shared account.</summary>
    public static string SourceKey(string login, string? sharedAccount) =>
        sharedAccount is null ? login : $"{login}#{sharedAccount}";

    /// <summary>The destination in another account for the folder the user picked: the same role, else the same name (case-insensitive).</summary>
    public static string? MatchMailbox(IReadOnlyList<JsonNode> mailboxes, string role, string name)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);
        var byRole = string.IsNullOrEmpty(role) ? null : mailboxes.FirstOrDefault(m => string.Equals(Str(m, "role"), role, StringComparison.Ordinal));
        var found = byRole ?? mailboxes.FirstOrDefault(m =>
            !string.IsNullOrEmpty(name) && Str(m, "name") is { } n && string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        return found is null ? null : Str(found, "id");
    }

    /// <summary>Encodes a "move to the matching folder" action: "moveto:" + role + U+001F + name.</summary>
    public static string MoveToAction(string role, string name) => $"moveto:{role}\u001f{name}";

    /// <summary>The parts of a <see cref="MoveToAction"/> payload (after "moveto:"): role and name.</summary>
    public static (string Role, string Name) ParseMoveTo(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var cut = spec.IndexOf('\u001f', StringComparison.Ordinal);
        return cut < 0 ? (string.Empty, spec) : (spec[..cut], spec[(cut + 1)..]);
    }

    /// <summary>The source's inbox: by role, else a folder named Inbox (Rust unified.rs:290 inbox_of).</summary>
    public static JsonNode? InboxOf(IReadOnlyList<JsonNode> mailboxes)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);
        return mailboxes.FirstOrDefault(m => string.Equals(Str(m, "role"), "inbox", StringComparison.Ordinal))
            ?? mailboxes.FirstOrDefault(m => Str(m, "name") is { } n && string.Equals(n, "inbox", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The parsed receivedAt in milliseconds, or <see cref="long.MinValue"/> when absent or unreadable (Rust unified.rs:336 ts).</summary>
    public static long Timestamp(JsonObject message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Str(message, "receivedAt") is { } s
            && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d.ToUnixTimeMilliseconds()
            : long.MinValue;
    }

    private static string? Str(JsonNode? node, string name) =>
        node is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
