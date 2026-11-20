namespace Barbican.Core.Compose;

/// <summary>The accepted raw submission and its account ownership.</summary>
public sealed record RawMessageReceipt(string LocalAccountKey, string JmapAccountId, string? EmailId, string SubmissionId, DateTimeOffset? ReleaseAt)
{
    /// <summary>Whether a delayed send was requested, independent of whether the server reported a time.</summary>
    public bool Scheduled { get; init; }
    public IReadOnlyList<RawRejectedRecipient> RejectedRecipients { get; init; } = [];
}
