// Mirrors: Rust main.rs finish_login (the sign-in screen gives way to the mail view).

using Barbican.Core.Accounts;

namespace Barbican.Presentation.Accounts;

/// <summary>A sign-in succeeded.</summary>
public sealed class SignedInEventArgs(AccountInfo account) : EventArgs
{
    public AccountInfo Account { get; } = account;
}
