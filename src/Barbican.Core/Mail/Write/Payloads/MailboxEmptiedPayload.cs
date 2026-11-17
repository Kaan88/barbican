// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:141 (payload of onMailboxEmpty); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onMailboxEmpty.</summary>
public sealed record MailboxEmptiedPayload(string MailboxId, string? Role, int Count);
