using System.Text.Json;

namespace Barbican.Core.OrganizationPolicy;

/// <summary>A bounded policy read; only an authoritative result carries a policy and coverage domains.</summary>
public sealed record OrganizationPolicyTransportResult(OrganizationPolicyTransportStatus Status, JsonElement? Policy = null, IReadOnlyList<string>? CoverageDomains = null)
{
    /// <summary>Domains selected by the server for this account.</summary>
    public IReadOnlyList<string> Domains => CoverageDomains ?? [];
}
