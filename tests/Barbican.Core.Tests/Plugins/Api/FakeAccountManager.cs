// Test double: a fixed list of signed-in accounts and which one is active.
using Barbican.Core.Accounts;
using Barbican.Core.Common;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class FakeAccountManager(IReadOnlyList<AccountInfo> accounts, string activeKey) : IAccountManager
{
    public IReadOnlyList<AccountInfo> Accounts => accounts;

    public AccountInfo? Active => accounts.FirstOrDefault(a => string.Equals(a.Key, activeKey, StringComparison.Ordinal));

    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged { add { } remove { } }

    public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

    public IAccountSession? SessionFor(string accountKey) => null;
}
