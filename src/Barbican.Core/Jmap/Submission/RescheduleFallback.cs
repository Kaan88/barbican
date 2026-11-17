// Mirrors: Rust jmap.rs:1449 RescheduleFallback. Stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Submission;

/// <summary>What to try when a scheduled send is rescheduled and the first attempt fails (closed hierarchy).</summary>
public abstract record RescheduleFallback
{
    private protected RescheduleFallback()
    {
    }
}
