// Mirrors: Bulwark lib/plugin-hook-catalog.ts:111 onAfterEmailSend ({ emailId, accountId, submissionId, to, cc, bcc, subject, scheduledFor, composerId }). Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The server accepted a message.</summary>
public sealed record EmailSentPayload(string EmailId, string AccountId, string SubmissionId, string To, string Cc, string Bcc, string Subject, DateTimeOffset? ScheduledFor);
