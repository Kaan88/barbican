// Mirrors: Rust jmap.rs apply_list_delta :1540. Stub from F2; owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>The watched list changed incrementally.</summary>
public sealed record ListDeltaEvent(ListDelta Delta) : MailSyncEvent
{
    /// <summary>The watched query the delta belongs to (a consumer drops an event of a list it no longer shows).</summary>
    public ListQuery? Query { get; init; }
}
