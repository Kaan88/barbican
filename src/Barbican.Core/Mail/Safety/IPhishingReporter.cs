// Mirrors: Bulwark lib/phishing-report.ts sendPhishingReport :105 (the seam the report flow sends through). Owner reader.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Mail.Safety;

/// <summary>Sends the phishing report of one message.</summary>
public interface IPhishingReporter
{
    /// <summary>Sends one report for <paramref name="message"/> to the address of <paramref name="setup"/>; a failure names why nothing went out.</summary>
    Task<Result<Unit>> ReportAsync(MailTarget message, PhishingReportPolicy setup, CancellationToken ct);
}
