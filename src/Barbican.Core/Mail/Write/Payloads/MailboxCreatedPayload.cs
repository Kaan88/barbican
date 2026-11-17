// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:135 (payload of onMailboxCreate); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onMailboxCreate.</summary>
public sealed record MailboxCreatedPayload(string MailboxId, string Name, string? ParentId);
