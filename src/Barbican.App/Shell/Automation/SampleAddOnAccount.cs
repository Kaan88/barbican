#if WINDOWS && AUTOMATION
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.App.Shell.Automation;

/// <summary>Exposes the in-process sample mailbox to the Windows add-on integration walkthrough.</summary>
internal sealed class SampleAddOnAccount(SampleActiveMail mail) : IAccountOverview, IAccountSessionSnapshots, IAccountManager
{
    private readonly Lock _gate = new();
    private readonly IAccountSession _session = mail.CreateAddOnSession();
    private bool _revoked;

    public IReadOnlyList<AccountInfo> Accounts => [_session.Account];
    public AccountInfo? Active => Volatile.Read(ref _revoked) ? null : _session.Account;
    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged { add { } remove { } }
    public IAccountSession? SessionFor(string accountKey) =>
        !Volatile.Read(ref _revoked) && string.Equals(accountKey, _session.Account.Key, StringComparison.Ordinal) ? _session : null;
    public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => RefusedAsync<AccountInfo>();
    public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => RefusedAsync<AccountInfo>();
    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => RefusedAsync<Unit>();
    private static Task<Result<T>> RefusedAsync<T>() => Task.FromResult(Result<T>.Fail(new ValidationError("The sample account is fixed.")));

    public IReadOnlyList<AccountStatus> Statuses => [new(_session.Account, Active is not null, true, Active is not null, null, null)];

    public event EventHandler? StatusesChanged { add { } remove { } }

    public Result<AccountSessionSnapshot> CaptureActiveSession() => Active is not null
        ? Result<AccountSessionSnapshot>.Ok(new(_session, 1)) : Unavailable();

    public Result<AccountSessionSnapshot> CaptureOwnerSession(string localAccountKey) =>
        SessionFor(localAccountKey) is not null ? Result<AccountSessionSnapshot>.Ok(new(_session, null)) : Unavailable();

    private static Result<AccountSessionSnapshot> Unavailable() =>
        Result<AccountSessionSnapshot>.Fail(new ValidationError("The sample account is unavailable."));

    public bool IsSessionCurrent(AccountSessionSnapshot snapshot) =>
        !Volatile.Read(ref _revoked) && ReferenceEquals(snapshot.Session, _session) && snapshot.ActiveSelectionGeneration is null or 1;

    public bool TryRunWhileCurrent(IReadOnlyList<AccountSessionSnapshot> snapshots, Func<bool> write)
    {
        lock (_gate) return snapshots.All(IsSessionCurrent) && write();
    }

    internal void RevokeOwner()
    {
        lock (_gate) Volatile.Write(ref _revoked, true);
    }
}
#endif
