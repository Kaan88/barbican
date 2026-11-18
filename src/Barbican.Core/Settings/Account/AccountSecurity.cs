// Mirrors: see IAccountSecurity. Stub from the M6 plan section 4.5; owner set-data replaces the class (constructor dependencies: IAccountManager, IHookBus).

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>The default <see cref="IAccountSecurity"/>.</summary>
public sealed class AccountSecurity : IAccountSecurity
{
    public bool IsAvailable(string accountKey) => throw new PortPendingException("AccountSecurity.IsAvailable");

    public Task<Result<Unit>> ChangePasswordAsync(string accountKey, string current, string newPassword, CancellationToken ct) => throw new PortPendingException("AccountSecurity.ChangePasswordAsync");

    public Task<Result<AccountTotp>> TotpAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AccountSecurity.TotpAsync");

    public Task<Result<AccountTotp>> BeginTotpAsync(string accountKey, string password, CancellationToken ct) => throw new PortPendingException("AccountSecurity.BeginTotpAsync");

    public Task<Result<Unit>> ConfirmTotpAsync(string accountKey, string code, CancellationToken ct) => throw new PortPendingException("AccountSecurity.ConfirmTotpAsync");

    public Task<Result<Unit>> DisableTotpAsync(string accountKey, string password, CancellationToken ct) => throw new PortPendingException("AccountSecurity.DisableTotpAsync");

    public Task<Result<IReadOnlyList<AccountAppPassword>>> AppPasswordsAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AccountSecurity.AppPasswordsAsync");

    public Task<Result<AccountIssuedSecret>> CreateAppPasswordAsync(string accountKey, string name, DateTimeOffset? expires, CancellationToken ct) => throw new PortPendingException("AccountSecurity.CreateAppPasswordAsync");

    public Task<Result<Unit>> DeleteAppPasswordAsync(string accountKey, string id, CancellationToken ct) => throw new PortPendingException("AccountSecurity.DeleteAppPasswordAsync");

    public Task<Result<IReadOnlyList<AccountApiKey>>> ApiKeysAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AccountSecurity.ApiKeysAsync");

    public Task<Result<AccountIssuedSecret>> CreateApiKeyAsync(string accountKey, string description, DateTimeOffset? expires, CancellationToken ct) => throw new PortPendingException("AccountSecurity.CreateApiKeyAsync");

    public Task<Result<Unit>> DeleteApiKeyAsync(string accountKey, string id, CancellationToken ct) => throw new PortPendingException("AccountSecurity.DeleteApiKeyAsync");

    public Task<Result<string?>> DisplayNameAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AccountSecurity.DisplayNameAsync");

    public Task<Result<Unit>> SetDisplayNameAsync(string accountKey, string name, CancellationToken ct) => throw new PortPendingException("AccountSecurity.SetDisplayNameAsync");

    public Task<Result<AccountSecurityEncryption>> EncryptionAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AccountSecurity.EncryptionAsync");

    public Task<Result<Unit>> SetEncryptionAsync(string accountKey, bool enabled, string? publicKey, CancellationToken ct) => throw new PortPendingException("AccountSecurity.SetEncryptionAsync");
}
