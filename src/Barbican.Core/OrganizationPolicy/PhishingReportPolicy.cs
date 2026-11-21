// Mirrors: Bulwark lib/phishing-report.ts:36-46 (PhishingReportSetup, phishingReportSetup: no address means reporting is off).
namespace Barbican.Core.OrganizationPolicy;

/// <summary>Where "Report phishing" sends and in which format; <see cref="Off"/> while the admin set no address.</summary>
public sealed record PhishingReportPolicy(string Address, PhishingReportFormat Format)
{
    public static PhishingReportPolicy Off { get; } = new(string.Empty, PhishingReportFormat.Forward);

    public bool IsOn => Address.Length > 0;
}
