// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:120 (payload of onEmailUnarchive); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onEmailUnarchive.</summary>
public sealed record EmailUnarchivePayload(IReadOnlyList<string> EmailIds, string ToMailboxId);
