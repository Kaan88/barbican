// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:140 (payload of onBeforeMailboxEmpty); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeMailboxEmpty.</summary>
public sealed record MailboxEmptyPayload(string MailboxId, string? Role);
