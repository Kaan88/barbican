// Mirrors: Rust compose.rs:868 send, schedule.rs:5-8 (HOLDFOR); Bulwark lib/jmap/client.ts sendEmail (holdFor), rescheduleEmailSubmission; RFC 8621 section 7.5, RFC 4865 (FUTURERELEASE). Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>How to submit: now, held for the undo window, or released at a time. At most one of the two holds applies; <see cref="ScheduledAt"/> wins.</summary>
public sealed record SendOptions
{
    /// <summary>Seconds to hold for undo (<c>sendDelaySeconds</c>; 0 sends at once). Clamped to the account's <c>maxDelayedSend</c>; ignored when the server has no FUTURERELEASE.</summary>
    public int UndoSeconds { get; init; }

    /// <summary>Release time of a scheduled send (sent as HOLDFOR, computed just before the request).</summary>
    public DateTimeOffset? ScheduledAt { get; init; }

    /// <summary>The user's choice for this send. A missing choice selects the effective default.</summary>
    public ComposerClassificationChoice? ClassificationChoice { get; init; }
}
