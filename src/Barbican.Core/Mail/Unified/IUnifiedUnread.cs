// Mirrors: M2 plan section 4.6 (IUnifiedUnread); Rust unified.rs:139 sum_other_logins, :400. Contract from stage F of M2; owner unified.

namespace Barbican.Core.Mail.Unified;

/// <summary>The unread total of the signed-in accounts that are not on screen (the unified row's badge).</summary>
public interface IUnifiedUnread
{
    long Total { get; }

    event EventHandler? Changed;
}
