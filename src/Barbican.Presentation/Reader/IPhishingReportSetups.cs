// Mirrors: Bulwark lib/phishing-report-flow.ts phishingSetupForEmail :34 (the report setup is the one of the policy of the account the message is reached
// through; no address means reporting is off) and hooks/use-phishing-report.ts (available while an address is set). Owner reader.

using Barbican.Core.OrganizationPolicy;

namespace Barbican.Presentation.Reader;

/// <summary>Where "Report phishing" sends for each account, from the organisation policy.</summary>
public interface IPhishingReportSetups
{
    /// <summary>What is known now for the account (off while unknown); a stale answer starts a refresh in the background.</summary>
    PhishingReportPolicy Cached(string accountKey);

    /// <summary>Reads the policy of the account now.</summary>
    Task<PhishingReportPolicy> ReadAsync(string accountKey, CancellationToken ct);
}
