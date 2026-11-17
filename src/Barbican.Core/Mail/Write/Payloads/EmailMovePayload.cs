// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:116 (payload of onBeforeEmailMove, onAfterEmailMove (ToMailboxId null: removed from FromMailboxId only)); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeEmailMove, onAfterEmailMove (ToMailboxId null: removed from FromMailboxId only).</summary>
public sealed record EmailMovePayload(IReadOnlyList<string> EmailIds, string? FromMailboxId, string? ToMailboxId);
