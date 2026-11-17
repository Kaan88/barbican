// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>One page of a list with the states needed for deltas (Rust jmap.rs ListPage).</summary>
public sealed record ListPage
{
    public IReadOnlyList<EmailSummary> Items { get; init; } = [];

    public int? Total { get; init; }

    public ListCursor? Next { get; init; }

    public string QueryState { get; init; } = "";

    public string EmailState { get; init; } = "";

    public string ThreadState { get; init; } = "";
}
