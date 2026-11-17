// Mirrors: Rust jmap.rs:1454 reschedule_fallbacks. Stub from F2; owner jmap.

using Barbican.Core.Common;

namespace Barbican.Core.Jmap.Submission;

/// <summary>Orders the fallbacks of a reschedule.</summary>
public static class ReschedulePlanner
{
    /// <summary>Recreate at the old time while it is still ahead, then restore the draft.</summary>
    public static IReadOnlyList<RescheduleFallback> Fallbacks(long? oldAtMs, long nowMs) =>
        throw new PortPendingException("ReschedulePlanner.Fallbacks: jmap stream");
}
