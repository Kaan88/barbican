// Mirrors: Rust oauth.rs accounts()/remember(), main.rs account switching; M1-plan §4.2. Contract stub from F2; owner accounts.

using Barbican.Core.Common;

namespace Barbican.Core.Accounts;

/// <summary>The signed-in accounts and which one is active.</summary>
public interface IAccountManager
{
    IReadOnlyList<AccountInfo> Accounts { get; }

    AccountInfo? Active { get; }

    event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged;

    /// <summary>Password sign-in, including the TOTP second step.</summary>
    Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct);

    /// <summary>OAuth sign-in (discovery, dynamic registration, loopback).</summary>
    Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct);

    /// <summary>Makes another signed-in account the active one.</summary>
    Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct);

    /// <summary>The session of an account, or null when it is not signed in.</summary>
    IAccountSession? SessionFor(string accountKey);
}
