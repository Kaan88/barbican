// Mirrors: Rust jmap.rs:1465 scheduled_hold, :1475 delayed_send_limit; Bulwark lib/jmap/client.ts hasSubmissionExtension (~7370,
// case-insensitive, object or array form); RFC 8621 §1.3.2 (maxDelayedSend, submissionExtensions), RFC 4865 (FUTURERELEASE, HOLDFOR).
// Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Submission;

/// <summary>Delayed and scheduled send limits.</summary>
public static class DelayedSend
{
    private const long StalwartAdvertised = 30L * 86400;
    private const long MtaDefault = 7L * 86400;

    /// <summary><c>maxDelayedSend</c>, or 0 unless FUTURERELEASE is listed; Stalwart's advertised 30 days is clamped to the 7-day MTA default.</summary>
    public static long Limit(JsonObject submissionCapability, bool stalwart)
    {
        ArgumentNullException.ThrowIfNull(submissionCapability);
        var max = (long)Math.Min(submissionCapability.Number("maxDelayedSend"), long.MaxValue);
        static bool Future(string k) => string.Equals(k, "FUTURERELEASE", StringComparison.OrdinalIgnoreCase);
        var listed = submissionCapability["submissionExtensions"] switch
        {
            JsonObject o => o.Any(kv => Future(kv.Key)),
            JsonArray a => a.Any(k => k.AsString() is { } s && Future(s)),
            _ => false,
        };
        if (!listed)
        {
            return 0;
        }

        return stalwart && max == StalwartAdvertised ? MtaDefault : max;
    }

    /// <summary>Seconds to hold for a release at <paramref name="atMs"/> (rounded up), or a <see cref="ScheduleError"/>.</summary>
    public static Result<long> ScheduledHold(long atMs, long nowMs, long max)
    {
        if (max == 0)
        {
            return Result<long>.Fail(new ScheduleError("Scheduled send is not supported for this account."));
        }

        var secs = (long)Math.Ceiling((atMs - nowMs) / 1000.0);
        if (secs <= 0)
        {
            return Result<long>.Fail(new ScheduleError("Choose a future date and time."));
        }

        return secs > max ? Result<long>.Fail(new ScheduleError("This time is later than the server allows.")) : Result<long>.Ok(secs);
    }
}
