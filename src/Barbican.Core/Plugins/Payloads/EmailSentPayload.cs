// Mirrors: Bulwark lib/plugin-hook-catalog.ts:111 onAfterEmailSend ({ emailId, accountId, submissionId, to, cc, bcc, subject, scheduledFor, composerId }). Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The server accepted a message.</summary>
public sealed record EmailSentPayload(string EmailId, string AccountId, string SubmissionId, string To, string Cc, string Bcc, string Subject, DateTimeOffset? ScheduledFor)
{
    /// <summary>The JMAP mail account containing the sent email.</summary>
    public string? JmapAccountId { get; init; }

    /// <summary>The host-issued composer handle, if one was present.</summary>
    public string? ComposerId { get; init; }
}
