// Mirrors: Fable review 2 R2-01/R2-03 (a popover row of an account the server refused opens the sign-in form for it).

using Barbican.Core.Accounts;

namespace Barbican.Presentation.Accounts;

/// <summary>The user chose an account that must sign in again.</summary>
public sealed class SignInRequestedEventArgs(AccountStatus status) : EventArgs
{
    /// <summary>The account's row state (its server, login and why it must sign in again).</summary>
    public AccountStatus Status { get; } = status;
}
