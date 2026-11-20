namespace Barbican.Core.OrganizationPolicy;

/// <summary>A parsed full envelope. Transport trust and account coverage must be established separately.</summary>
public sealed record OrganizationPolicyDocument(OrganizationPolicyOverride Global, IReadOnlyList<OrganizationPolicyScope> Scopes);
