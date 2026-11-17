// Mirrors: Bulwark onMailboxChange (lib/plugin-hook-catalog.ts:75). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The user opened another folder.</summary>
public sealed record MailboxChangedPayload(string MailboxId, string? Role, string? PreviousMailboxId);
