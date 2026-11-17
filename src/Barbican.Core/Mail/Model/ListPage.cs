// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>One page of a list with the states needed for deltas (Rust jmap.rs ListPage).</summary>
public sealed record ListPage
{
    public IReadOnlyList<EmailSummary> Items { get; init; } = [];

    /// <summary>Ids the query returned that did not come back as a readable row (a message that failed to parse); a caller may log them.</summary>
    public IReadOnlyList<string> DroppedIds { get; init; } = [];

    public int? Total { get; init; }

    public ListCursor? Next { get; init; }

    public string QueryState { get; init; } = "";

    public string EmailState { get; init; } = "";

    public string ThreadState { get; init; } = "";

    /// <summary>Email/query <c>canCalculateChanges</c> (RFC 8620 §5.5): false means no delta may be asked from these states.</summary>
    public bool CanCalculateChanges { get; init; }

    /// <summary>Conversation sizes by thread id for the rows of this page (Thread/get <c>emailIds</c> counts).</summary>
    public IReadOnlyDictionary<string, int> Threads { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>The position of the first row of this page in the list (RFC 8620 §5.5 <c>position</c> of the response).</summary>
    public int Position { get; init; }
}
