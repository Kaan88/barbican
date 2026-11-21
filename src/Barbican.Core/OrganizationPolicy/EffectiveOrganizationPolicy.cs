namespace Barbican.Core.OrganizationPolicy;

public sealed record EffectiveOrganizationPolicy(bool NoPrint, bool Watermark, ClassificationPolicy Labels,
    IReadOnlyList<string> MatchedScopes, string NoPrintSource, string WatermarkSource, string LabelsSource)
{
    public ProofreadingPolicy Proofreading { get; init; } = ProofreadingPolicy.Empty;

    /// <summary>The phishing report setup; <see cref="PhishingReportPolicy.Off"/> while no address is set.</summary>
    public PhishingReportPolicy PhishingReport { get; init; } = PhishingReportPolicy.Off;
}
