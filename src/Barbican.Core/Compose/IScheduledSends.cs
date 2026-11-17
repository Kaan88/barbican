// Mirrors: Rust schedule.rs:215-292 (banner, action, reschedule), jmap.rs reschedule_submission :985 / ReschedulePlanner; Bulwark lib/jmap/client.ts rescheduleEmailSubmission, cancelEmailSubmission, message-actions.ts; RFC 8621 section 7.3, 7.5. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The Scheduled view's data and its actions.</summary>
public interface IScheduledSends
{
    /// <summary>The longest hold in seconds the account's submission capability allows (maxDelayedSend with FUTURERELEASE, Stalwart's 30 days clamped to 7); 0 when the server cannot hold or the account is not signed in. Additive (reported): the schedule dialog needs it.</summary>
    long MaxDelayedSend(string accountId) => 0;

    Task<Result<IReadOnlyList<ScheduledMessage>>> ListAsync(string accountId, CancellationToken ct);

    /// <summary>Reschedule (the submission's undo path, then the fallbacks of <c>ReschedulePlanner</c>; B-JMAP-038/039).</summary>
    Task<Result<Unit>> RescheduleAsync(string accountId, string submissionId, DateTimeOffset at, CancellationToken ct);

    /// <summary>"Cancel send": undoStatus canceled, the message stays in Drafts. Returns the draft's email id.</summary>
    Task<Result<string>> CancelAsync(string accountId, string submissionId, CancellationToken ct);
}
