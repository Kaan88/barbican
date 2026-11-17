// Mirrors: Rust jmap.rs:1465 scheduled_hold, :1475 delayed_send_limit; Bulwark hasDelayedSend; RFC 8621 §1.3.2, FUTURERELEASE (RFC 4865).
// Stub from F2; owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Jmap.Submission;

/// <summary>Delayed and scheduled send limits.</summary>
public static class DelayedSend
{
    /// <summary><c>maxDelayedSend</c>, or 0 unless FUTURERELEASE is listed; Stalwart's 30 days is clamped to 7.</summary>
    public static long Limit(JsonObject submissionCapability, bool stalwart) => throw new PortPendingException("DelayedSend.Limit: jmap stream");

    /// <summary>Seconds to hold for a release time (rounded up), or a <see cref="ScheduleError"/>.</summary>
    public static Result<long> ScheduledHold(long atMs, long nowMs, long max) => throw new PortPendingException("DelayedSend.ScheduledHold: jmap stream");
}
