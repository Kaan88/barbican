// Mirrors: Bulwark lib/plugin-sandbox/storage-scope.ts:82-100 and stores/auth-store.ts sign-out cleanup.
using Barbican.Core.Common;

namespace Barbican.Core.Accounts;

/// <summary>Erases profile data after storage captures are fenced and before sign-out detaches the accounts.</summary>
public interface IAccountSignOutCleanup
{
    /// <summary>Holds the profile's storage authority before a login is established or restored.</summary>
    Task<bool> TryHoldStorageAuthorityAsync(CancellationToken ct) => Task.FromResult(false);

    Task<Result<Unit>> PrepareAsync(IReadOnlyList<string> localAccountKeys, bool allAccounts, CancellationToken ct);
}
