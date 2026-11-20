// Raw RFC 822 send data supplied by an authorized host caller.
namespace Barbican.Core.Compose;

/// <summary>A raw message and its host-approved envelope.</summary>
public sealed record RawMessageRequest(string LocalAccountKey, string IdentityId, byte[] Bytes, IReadOnlyList<string> EnvelopeRecipients)
{
    public DateTimeOffset? ScheduledAt { get; init; }
    public bool SubmitOnly { get; init; }
}
