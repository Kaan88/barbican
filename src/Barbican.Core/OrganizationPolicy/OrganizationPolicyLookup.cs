namespace Barbican.Core.OrganizationPolicy;

/// <summary>A current policy snapshot or a reason why it could not be established.</summary>
public sealed record OrganizationPolicyLookup(OrganizationPolicyTransportStatus Status, OrganizationPolicySnapshot? Snapshot = null);
