namespace Barbican.Core.OrganizationPolicy;

/// <summary>Values left null inherit the preceding device policy.</summary>
public sealed record OrganizationPolicyOverride(bool? NoPrint, bool? Watermark, ClassificationPolicy? Labels)
{
    public ProofreadingPolicy Proofreading { get; init; } = ProofreadingPolicy.Empty;

    /// <summary>Where "Report phishing" sends (Bulwark organisation.phishingReport.address; "" turns it off); null inherits.</summary>
    public string? PhishingReportAddress { get; init; }

    /// <summary>How the report is built (forward or ARF); null inherits.</summary>
    public PhishingReportFormat? PhishingReportFormat { get; init; }
}
