// Mirrors: Bulwark components/email/email-context-menu.tsx:371-379 (the copy list offers, per other account, its folders; a pick copies there)
// and hooks/use-message-file-actions.ts:181-195. Barbican opens a dialog picker where Bulwark opens a submenu. G-048. Owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>What the copy picker answered: the destination account and folder.</summary>
/// <param name="AccountKey">The account key of the login.</param>
/// <param name="MailboxId">The mailbox id in that account.</param>
public sealed record CopyToAccountAnswer(string AccountKey, string MailboxId);
