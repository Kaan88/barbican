// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:331 (payload of onEmailDrop); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onEmailDrop.</summary>
public sealed record EmailDropPayload(IReadOnlyList<string> EmailIds, string TargetMailboxId);
