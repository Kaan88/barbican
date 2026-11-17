// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>What a list shows: a mailbox or a search (Email/query filter, sort and the unread-first section).</summary>
public sealed record ListQuery
{
    public string? MailboxId { get; init; }

    public string? SearchText { get; init; }

    public int PageSize { get; init; } = 50;

    /// <summary>Only messages with this keyword (Bulwark getEmails <c>hasKeyword</c>; the read part of the unread-first Inbox).</summary>
    public string? HasKeyword { get; init; }

    /// <summary>Only messages without this keyword (the unread section of the unread-first Inbox, <c>notKeyword: $seen</c>).</summary>
    public string? NotKeyword { get; init; }

    /// <summary>An extra Email/query filter, AND-ed with the mailbox and keyword conditions by ListFilters (search, wave 2; null by default so M1 behaves as before).</summary>
    public System.Text.Json.Nodes.JsonObject? Filter { get; init; }

    /// <summary>An explicit sort (search, wave 2); null keeps the list's own order.</summary>
    public IReadOnlyList<SortComparator>? Sort { get; init; }

    /// <summary>collapseThreads (RFC 8621 section 4.4.3, J-051); null keeps the server default.</summary>
    public bool? CollapseThreads { get; init; }
}
