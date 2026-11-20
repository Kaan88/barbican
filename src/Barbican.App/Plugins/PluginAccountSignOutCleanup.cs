#if WINDOWS
using Barbican.Core.Accounts;
using Barbican.Core.Common;

namespace Barbican.App.Plugins;

// Resolve lazily because the package host uses the account overview.
internal sealed class PluginAccountSignOutCleanup(Func<PluginPackageHost> packages) : IAccountSignOutCleanup
{
    public Task<bool> TryHoldStorageAuthorityAsync(CancellationToken ct) => packages().TryHoldStorageAuthorityAsync(ct);

    public Task<Result<Unit>> PrepareAsync(IReadOnlyList<string> localAccountKeys, bool allAccounts, CancellationToken ct) =>
        packages().PrepareSignOutAsync(localAccountKeys, allAccounts, ct);
}
#endif
