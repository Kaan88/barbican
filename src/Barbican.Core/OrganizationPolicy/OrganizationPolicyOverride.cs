namespace Barbican.Core.OrganizationPolicy;

/// <summary>Values left null inherit the preceding device policy.</summary>
public sealed record OrganizationPolicyOverride(bool? NoPrint, bool? Watermark, ClassificationPolicy? Labels)
{
    public ProofreadingPolicy Proofreading { get; init; } = ProofreadingPolicy.Empty;
}
