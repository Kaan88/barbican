// Mirrors: Bulwark lib/proofreading/gate.ts:42-48. No Stalwart counterpart.
namespace Barbican.Core.OrganizationPolicy;

public sealed record ProofreadingRestriction(bool Locked, bool? Value);
