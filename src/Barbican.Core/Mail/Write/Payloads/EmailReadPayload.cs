// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:123 (payload of onBeforeEmailReadStateChange, onEmailReadStateChange); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeEmailReadStateChange, onEmailReadStateChange.</summary>
public sealed record EmailReadPayload(IReadOnlyList<string> EmailIds, bool Read);
