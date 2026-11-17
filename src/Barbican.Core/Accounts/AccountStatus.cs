// Mirrors: Bulwark components/layout/account-switcher.tsx:455-:500 (a row: avatar, check on the active one, default star, inbox unread,
// the server line for an account in trouble), stores/account-store.ts AccountEntry (isConnected, hasError, isDefault). spec I-006.

namespace Barbican.Core.Accounts;

/// <summary>What the account popover shows about one account.</summary>
/// <param name="Account">The account.</param>
/// <param name="IsActive">The account on screen.</param>
/// <param name="IsDefault">The default account.</param>
/// <param name="IsConnected">Its session is up.</param>
/// <param name="InboxUnread">Unread in its own inbox; null while not known (no badge), distinct from a known zero.</param>
/// <param name="Error">Why it is not connected (for example "needs to sign in again"), or null.</param>
public sealed record AccountStatus(AccountInfo Account, bool IsActive, bool IsDefault, bool IsConnected, int? InboxUnread, string? Error)
{
    /// <summary>The server refused the account's credentials: only a new sign-in brings it back (Fable review 2 R2-01).</summary>
    public bool NeedsSignIn { get; init; }

    /// <summary>The account is not connected and is being tried again (no network, or its server is down; R2-02).</summary>
    public bool IsConnecting => !IsConnected && !NeedsSignIn;
}
