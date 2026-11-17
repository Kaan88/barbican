// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:127 (payload of onBeforeEmailKeywordChange, onEmailKeywordChange); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeEmailKeywordChange, onEmailKeywordChange.</summary>
public sealed record EmailKeywordPayload(IReadOnlyList<string> EmailIds, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);
