// Mirrors: Rust jmap.rs parse_changes :1374 (cannotCalculateChanges, a state that went backwards: reload). Stub from F2; owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>No delta can be computed: reload the watched list.</summary>
public sealed record ListResetEvent : MailSyncEvent
{
    /// <summary>The watched query to reload (null: whatever list is open).</summary>
    public ListQuery? Query { get; init; }
}
