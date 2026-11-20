#if WINDOWS && AUTOMATION
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.App.Shell.Automation;

/// <summary>Exposes the in-process sample mailbox to the Windows add-on integration walkthrough.</summary>
internal sealed class SampleAddOnAccount(SampleActiveMail mail) : IAccountOverview, IAccountSessionSnapshots, IAccountManager
{
    private readonly IAccountSession _session = mail.CreateAddOnSession();

    public IReadOnlyList<AccountInfo> Accounts => [_session.Account];
    public AccountInfo? Active => _session.Account;
    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged { add { } remove { } }
    public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, _session.Account.Key, StringComparison.Ordinal) ? _session : null;
    public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => RefusedAsync<AccountInfo>();
    public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => RefusedAsync<AccountInfo>();
    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => RefusedAsync<Unit>();
    private static Task<Result<T>> RefusedAsync<T>() => Task.FromResult(Result<T>.Fail(new ValidationError("The sample account is fixed.")));

    public IReadOnlyList<AccountStatus> Statuses => [new(_session.Account, true, true, true, null, null)];

    public event EventHandler? StatusesChanged { add { } remove { } }

    public Result<AccountSessionSnapshot> CaptureActiveSession() => Result<AccountSessionSnapshot>.Ok(new(_session, 1));

    public Result<AccountSessionSnapshot> CaptureOwnerSession(string localAccountKey) =>
        string.Equals(_session.Account.Key, localAccountKey, StringComparison.Ordinal)
            ? Result<AccountSessionSnapshot>.Ok(new(_session, null))
            : Result<AccountSessionSnapshot>.Fail(new ValidationError("The sample account is unavailable."));

    public bool IsSessionCurrent(AccountSessionSnapshot snapshot) =>
        ReferenceEquals(snapshot.Session, _session) && snapshot.ActiveSelectionGeneration is null or 1;

    public bool TryRunWhileCurrent(IReadOnlyList<AccountSessionSnapshot> snapshots, Func<bool> write) =>
        snapshots.All(IsSessionCurrent) && write();
}
#endif
