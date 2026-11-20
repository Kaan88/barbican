namespace Barbican.Core.Compose;

/// <summary>Only facts observed from the accepted submission, never estimated.</summary>
internal sealed record RawSubmissionObservation(DateTimeOffset? SendAt, IReadOnlyList<RawRejectedRecipient> RejectedRecipients, bool AllRefused, RawDeliveryStatus Status = RawDeliveryStatus.Missing)
{
    public IReadOnlyList<string> ExpectedRecipients { get; init; } = [];
    public IReadOnlyList<string> DeliveredRecipients { get; init; } = [];
}
