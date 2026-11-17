// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:121 (payload of onBeforeEmailSpamToggle, onEmailSpamToggle); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeEmailSpamToggle, onEmailSpamToggle.</summary>
public sealed record EmailSpamPayload(IReadOnlyList<string> EmailIds, bool Spam);
