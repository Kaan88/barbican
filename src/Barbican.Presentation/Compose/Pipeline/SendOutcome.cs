// Mirrors: see SendOutcomeStatus. Owner send-pipeline.

using Barbican.Core.Compose;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>What <see cref="SendCoordinator.RunAsync"/> did: the status, the receipt of an accepted message, and the user text of a refusal.</summary>
public sealed record SendOutcome(SendOutcomeStatus Status)
{
    public SendReceipt? Receipt { get; init; }

    public string? Message { get; init; }

    /// <summary>The entries that are no address (<see cref="SendOutcomeStatus.InvalidAddresses"/>).</summary>
    public IReadOnlyList<string> InvalidAddresses { get; init; } = [];

    public bool IsAccepted => Status is SendOutcomeStatus.Sent or SendOutcomeStatus.Scheduled;
}
