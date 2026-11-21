// Mirrors: Bulwark lib/admin/organisation-policy.ts:75 PHISHING_REPORT_FORMATS ('forward' | 'arf').
namespace Barbican.Core.OrganizationPolicy;

/// <summary>How a phishing report is built: the message forwarded as an attachment, or an ARF report (RFC 5965).</summary>
public enum PhishingReportFormat
{
    Forward,
    Arf,
}
