namespace Barbican.Core.OrganizationPolicy;

public sealed record OrganizationPolicyScope(string Id, IReadOnlyList<string> Domains, IReadOnlyList<string> Groups, OrganizationPolicyOverride Values);
