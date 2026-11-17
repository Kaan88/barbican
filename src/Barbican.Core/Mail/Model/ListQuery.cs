// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>What a list shows: a mailbox or a search (Email/query filter, sort and the unread-first section).</summary>
public sealed record ListQuery
{
    public string? MailboxId { get; init; }

    public string? SearchText { get; init; }

    public int PageSize { get; init; } = 50;
}
