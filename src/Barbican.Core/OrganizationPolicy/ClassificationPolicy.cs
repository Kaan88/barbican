namespace Barbican.Core.OrganizationPolicy;

public sealed record ClassificationPolicy(IReadOnlyList<ClassificationLabel> List, string DefaultId, bool Required, string ExtraHeader);
