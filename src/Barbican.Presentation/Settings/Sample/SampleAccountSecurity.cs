// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of IAccountSecurity that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Settings.Account;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An in-memory <see cref="IAccountSecurity"/>: the password is "sample-password", two-step sign-in starts off.</summary>
public sealed class SampleAccountSecurity : IAccountSecurity
{
    private readonly Lock _lock = new();
    private readonly List<AccountAppPassword> _apps = [];
    private readonly List<AccountApiKey> _keys = [];
    private string _password = "sample-password";
    private bool _totp;
    private bool _encrypted;
    private string? _displayName = "Sample User";
    private int _next = 1;

    public bool IsAvailable(string accountKey) => true;

    public Task<Result<Unit>> ChangePasswordAsync(string accountKey, string current, string newPassword, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                if (!string.Equals(current, _password, StringComparison.Ordinal))
                {
                    return Result<Unit>.Fail(new ValidationError("The current password is not right."));
                }

                _password = newPassword;
                return Result<Unit>.Ok(default);
            }
        });

    public Task<Result<AccountTotp>> TotpAsync(string accountKey, CancellationToken ct) => SampleThreading.OffThreadAsync(() => Result<AccountTotp>.Ok(new AccountTotp(_totp, null)));

    public Task<Result<AccountTotp>> BeginTotpAsync(string accountKey, string password, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() => Result<AccountTotp>.Ok(new AccountTotp(false, "otpauth://totp/Sample:user@example.org?secret=SAMPLESECRET&issuer=Sample")));

    public Task<Result<Unit>> ConfirmTotpAsync(string accountKey, string code, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            _totp = true;
            return Result<Unit>.Ok(default);
        });

    public Task<Result<Unit>> DisableTotpAsync(string accountKey, string password, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            _totp = false;
            return Result<Unit>.Ok(default);
        });

    public Task<Result<IReadOnlyList<AccountAppPassword>>> AppPasswordsAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                return Result<IReadOnlyList<AccountAppPassword>>.Ok([.. _apps]);
            }
        });

    public Task<Result<AccountIssuedSecret>> CreateAppPasswordAsync(string accountKey, string name, DateTimeOffset? expires, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                var id = "ap" + _next++;
                _apps.Add(new AccountAppPassword(id, name, null, expires));
                return Result<AccountIssuedSecret>.Ok(new AccountIssuedSecret(id, "sample-secret-" + id));
            }
        });

    public Task<Result<Unit>> DeleteAppPasswordAsync(string accountKey, string id, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                _ = _apps.RemoveAll(a => string.Equals(a.Id, id, StringComparison.Ordinal));
                return Result<Unit>.Ok(default);
            }
        });

    public Task<Result<IReadOnlyList<AccountApiKey>>> ApiKeysAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                return Result<IReadOnlyList<AccountApiKey>>.Ok([.. _keys]);
            }
        });

    public Task<Result<AccountIssuedSecret>> CreateApiKeyAsync(string accountKey, string description, DateTimeOffset? expires, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                var id = "ak" + _next++;
                _keys.Add(new AccountApiKey(id, description, expires));
                return Result<AccountIssuedSecret>.Ok(new AccountIssuedSecret(id, "sample-key-" + id));
            }
        });

    public Task<Result<Unit>> DeleteApiKeyAsync(string accountKey, string id, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                _ = _keys.RemoveAll(a => string.Equals(a.Id, id, StringComparison.Ordinal));
                return Result<Unit>.Ok(default);
            }
        });

    public Task<Result<string?>> DisplayNameAsync(string accountKey, CancellationToken ct) => SampleThreading.OffThreadAsync(() => Result<string?>.Ok(_displayName));

    public Task<Result<Unit>> SetDisplayNameAsync(string accountKey, string name, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            _displayName = name;
            return Result<Unit>.Ok(default);
        });

    public Task<Result<AccountSecurityEncryption>> EncryptionAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() => Result<AccountSecurityEncryption>.Ok(new AccountSecurityEncryption(_encrypted, _encrypted ? "AES-256" : string.Empty)));

    public Task<Result<Unit>> SetEncryptionAsync(string accountKey, bool enabled, string? publicKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            _encrypted = enabled;
            return Result<Unit>.Ok(default);
        });
}
