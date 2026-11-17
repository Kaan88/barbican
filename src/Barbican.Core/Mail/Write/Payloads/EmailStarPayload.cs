// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:125 (payload of onBeforeEmailStarToggle, onEmailStarToggle); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeEmailStarToggle, onEmailStarToggle.</summary>
public sealed record EmailStarPayload(IReadOnlyList<string> EmailIds, bool Starred, string? AccountId);
