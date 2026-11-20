// Bulwark lib/admin/organisation-policy.ts classification data.
namespace Barbican.Core.OrganizationPolicy;

public sealed record ClassificationLabel(string Id, string Name, string Color, string Description, string Marking,
    string Sensitivity, string SubjectPrefix, string ExtraHeaderValue);
