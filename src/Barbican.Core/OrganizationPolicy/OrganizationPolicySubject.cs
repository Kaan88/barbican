namespace Barbican.Core.OrganizationPolicy;

/// <summary>An account address and its fully resolved group addresses.</summary>
public sealed record OrganizationPolicySubject(string Email, IReadOnlyList<string> Groups);
