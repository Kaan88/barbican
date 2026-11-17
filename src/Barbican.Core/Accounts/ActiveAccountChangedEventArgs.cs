// Mirrors: M1-plan §4.2; Bulwark hook onAccountSwitch. Contract stub from F2; owner accounts.

namespace Barbican.Core.Accounts;

/// <summary>The active account changed.</summary>
public sealed class ActiveAccountChangedEventArgs(AccountInfo? previous, AccountInfo? current) : EventArgs
{
    public AccountInfo? Previous { get; } = previous;

    public AccountInfo? Current { get; } = current;
}
