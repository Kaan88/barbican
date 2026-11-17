// Mirrors: Bulwark lib/plugin-hook-catalog.ts:108-109 onBeforeEmailSend / onTransformOutgoingEmail (OutgoingEmail). Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A message as it is about to be submitted: onBeforeEmailSend can stop it, onTransformOutgoingEmail returns it changed (recipients, subject, bodies).</summary>
public sealed record OutgoingEmailPayload(string AccountId, string IdentityId, string From, string To, string Cc, string Bcc, string Subject, string Html, string Text)
{
    public IReadOnlyList<string> AttachmentNames { get; init; } = [];

    public bool ReadReceipt { get; init; }

    /// <summary>The release time of a scheduled send, else null.</summary>
    public DateTimeOffset? ScheduledFor { get; init; }
}
