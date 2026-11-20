namespace Barbican.Core.OrganizationPolicy;

/// <summary>The trust and availability of a policy response.</summary>
public enum OrganizationPolicyTransportStatus
{
    Authoritative,
    Public,
    Missing,
    Untrusted,
    Unauthorized,
    Unavailable,
    Malformed,
    Stale,
}
