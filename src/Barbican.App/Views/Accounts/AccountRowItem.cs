// Mirrors: Bulwark components/layout/account-switcher.tsx:455-500 (row: avatar, check, server line); spec I-006. The default star, the unread badge and the error line
// need data IAccountManager does not carry yet (accounts stream: AccountsViewModel); they are added here when it provides them.

namespace Barbican.App.Views.Accounts;

/// <summary>One row of the account popover.</summary>
/// <param name="Key">The account key (IAccountManager.SwitchToAsync takes it).</param>
/// <param name="User">The login.</param>
/// <param name="Server">The server line.</param>
/// <param name="Initials">Two letters for the avatar.</param>
/// <param name="TintIndex">0..3, which tint the avatar takes.</param>
/// <param name="IsActive">The account in use (shown with a check).</param>
public sealed record AccountRowItem(string Key, string User, string Server, string Initials, int TintIndex, bool IsActive);
