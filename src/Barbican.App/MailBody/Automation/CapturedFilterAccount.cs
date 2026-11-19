#if AUTOMATION_WRITES || BARBICAN_FILTER_TESTS
// Captures one authorized session, so FilterService cannot follow active-account changes.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.App.MailBody.Automation;

internal sealed class CapturedFilterAccount(IAccountSession session) : IAccountManager
{
    public IReadOnlyList<AccountInfo> Accounts => [session.Account];
    public AccountInfo? Active => session.Account;
    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged { add { } remove { } }
    public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, session.Account.Key, StringComparison.Ordinal) ? session : null;
    public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => RefusedAsync<AccountInfo>();
    public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => RefusedAsync<AccountInfo>();
    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => RefusedAsync<Unit>();
    private static Task<Result<T>> RefusedAsync<T>() => Task.FromResult(Result<T>.Fail(new ValidationError("The verification account is fixed.")));
}
#endif
