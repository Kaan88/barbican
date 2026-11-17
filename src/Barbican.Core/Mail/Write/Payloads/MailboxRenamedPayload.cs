// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:137 (payload of onMailboxRename); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onMailboxRename.</summary>
public sealed record MailboxRenamedPayload(string MailboxId, string Name, string PreviousName);
