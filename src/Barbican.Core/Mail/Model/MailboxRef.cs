// Mirrors: M2 plan section 4.1 (MailboxRef). Contract from stage F of M2; owner actions.

namespace Barbican.Core.Mail.Model;

/// <summary>A mailbox in an account: where an action runs (the open folder) or what a menu is for.</summary>
public sealed record MailboxRef(string AccountKey, string AccountId, string MailboxId, string? Role);
