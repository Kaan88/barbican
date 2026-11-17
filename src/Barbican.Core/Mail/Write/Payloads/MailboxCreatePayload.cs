// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:134 (payload of onBeforeMailboxCreate); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeMailboxCreate.</summary>
public sealed record MailboxCreatePayload(string Name, string? ParentId);
