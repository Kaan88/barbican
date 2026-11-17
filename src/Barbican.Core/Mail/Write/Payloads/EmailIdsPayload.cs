// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:118 (payload of onBeforeEmailArchive, onEmailArchive); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeEmailArchive, onEmailArchive.</summary>
public sealed record EmailIdsPayload(IReadOnlyList<string> EmailIds);
