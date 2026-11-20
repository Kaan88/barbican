namespace Barbican.Core.Compose;

/// <summary>The accepted raw submission and its account ownership.</summary>
public sealed record RawMessageReceipt(string LocalAccountKey, string JmapAccountId, string? EmailId, string SubmissionId, DateTimeOffset? ReleaseAt);
