// Mirrors: see IAccountIdentities; owner set-data.

namespace Barbican.Core.Settings.Account;

/// <summary>The identities of an account changed.</summary>
public sealed class AccountIdentityChangedEventArgs(string accountKey) : EventArgs
{
    public string AccountKey { get; } = accountKey;
}
