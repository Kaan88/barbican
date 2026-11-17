// Mirrors: Bulwark lib/jmap/client.ts listScheduledSubmissions (EmailSubmission/query undoStatus pending), message-actions.ts; RFC 8621 section 7.3. Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>A held submission in the Scheduled view.</summary>
public sealed record ScheduledMessage(string EmailId, string SubmissionId, DateTimeOffset At);
