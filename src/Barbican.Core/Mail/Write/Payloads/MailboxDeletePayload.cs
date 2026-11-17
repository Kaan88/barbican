// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:138 (payload of onBeforeMailboxDelete, onMailboxDelete); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeMailboxDelete, onMailboxDelete.</summary>
public sealed record MailboxDeletePayload(string MailboxId, string Name);
