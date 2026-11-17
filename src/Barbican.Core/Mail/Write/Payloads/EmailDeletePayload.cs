// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:114 (payload of onBeforeEmailDelete, onAfterEmailDelete); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeEmailDelete, onAfterEmailDelete.</summary>
public sealed record EmailDeletePayload(IReadOnlyList<string> EmailIds, bool Permanent);
