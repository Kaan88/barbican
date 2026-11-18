// Mirrors: see AccountAppPassword. Live writes only on <test-account> and only in the integrate pass with the user's agent profile (PORT-PLAN M6: account security pages are coded against Stalwart's source; the admin key is never used, D-M6-A09).
// THREADING (D-M6-00): every call completes off the UI thread; there is no event (each call returns the new state). Contract from the M6 plan section 4.5; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>Password, two-step sign-in, app passwords and API keys of the signed-in account.</summary>
public interface IAccountSecurity
{
    /// <summary>Whether the server offers the account-security objects (Stalwart's x: methods under urn:stalwart:jmap); the page shows "not available" otherwise.</summary>
    bool IsAvailable(string accountKey);

    Task<Result<Unit>> ChangePasswordAsync(string accountKey, string current, string newPassword, CancellationToken ct);

    Task<Result<AccountTotp>> TotpAsync(string accountKey, CancellationToken ct);

    /// <summary>Starts enrolment: a new secret and its otpauth URI (nothing is active until <see cref="ConfirmTotpAsync"/>).</summary>
    Task<Result<AccountTotp>> BeginTotpAsync(string accountKey, string password, CancellationToken ct);

    Task<Result<Unit>> ConfirmTotpAsync(string accountKey, string code, CancellationToken ct);

    Task<Result<Unit>> DisableTotpAsync(string accountKey, string password, CancellationToken ct);

    Task<Result<IReadOnlyList<AccountAppPassword>>> AppPasswordsAsync(string accountKey, CancellationToken ct);

    Task<Result<AccountIssuedSecret>> CreateAppPasswordAsync(string accountKey, string name, DateTimeOffset? expires, CancellationToken ct);

    Task<Result<Unit>> DeleteAppPasswordAsync(string accountKey, string id, CancellationToken ct);

    Task<Result<IReadOnlyList<AccountApiKey>>> ApiKeysAsync(string accountKey, CancellationToken ct);

    Task<Result<AccountIssuedSecret>> CreateApiKeyAsync(string accountKey, string description, DateTimeOffset? expires, CancellationToken ct);

    Task<Result<Unit>> DeleteApiKeyAsync(string accountKey, string id, CancellationToken ct);

    /// <summary>The account's display name (x:AccountSettings description, the name Stalwart seeds the first identity from); null when unset. Hook onDisplayNameChange fires from <see cref="SetDisplayNameAsync"/>.</summary>
    Task<Result<string?>> DisplayNameAsync(string accountKey, CancellationToken ct);

    Task<Result<Unit>> SetDisplayNameAsync(string accountKey, string name, CancellationToken ct);

    Task<Result<AccountSecurityEncryption>> EncryptionAsync(string accountKey, CancellationToken ct);

    /// <summary>Turns encryption at rest on with the user's public key (PEM) or off; the server encrypts new mail only.</summary>
    Task<Result<Unit>> SetEncryptionAsync(string accountKey, bool enabled, string? publicKey, CancellationToken ct);
}
