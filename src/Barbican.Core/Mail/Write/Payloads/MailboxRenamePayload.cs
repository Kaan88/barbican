// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:136 (payload of onBeforeMailboxRename); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onBeforeMailboxRename.</summary>
public sealed record MailboxRenamePayload(string MailboxId, string Name);
