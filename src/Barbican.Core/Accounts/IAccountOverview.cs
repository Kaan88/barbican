// Mirrors: Bulwark components/layout/account-switcher.tsx (rows and the others-unread dot), lib/account-inbox-unread.ts. A separate
// interface so IAccountManager (M1-plan §4.2) stays as the contract says.

namespace Barbican.Core.Accounts;

/// <summary>The account rows with their state, for the account popover and the rail avatar.</summary>
public interface IAccountOverview
{
    /// <summary>One row per known account, most recently used first.</summary>
    IReadOnlyList<AccountStatus> Statuses { get; }

    /// <summary>Raised (on any thread) when a row changed.</summary>
    event EventHandler? StatusesChanged;
}
