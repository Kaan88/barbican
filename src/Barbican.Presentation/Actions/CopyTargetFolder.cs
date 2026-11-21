// Mirrors: Bulwark lib/move-menu.ts:115-118 (a copy target is a folder that takes mail and is not Drafts or Sent); G-048. Owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>One folder a copy can land in, as the picker rows show it.</summary>
/// <param name="MailboxId">The mailbox id in the destination account.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Depth">Nesting level.</param>
public sealed record CopyTargetFolder(string MailboxId, string Name, int Depth);
