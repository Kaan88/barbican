// Mirrors: M2 plan section 4.6 (IAccountMaintenance); Bulwark components/layout/account-switcher.tsx:442-675, lib/sign-out-cleanup.ts. Contract from stage F of M2; owner unified. R2-06 (vault under two instances) comes first.

using Barbican.Core.Common;

namespace Barbican.Core.Accounts;

/// <summary>Sign out, default account and order of the signed-in accounts.</summary>
public interface IAccountMaintenance
{
    /// <summary>Signs one account out (onBeforeLogout, onAfterLogout, onAccountRemove).</summary>
    Task<Result<Unit>> SignOutAsync(string accountKey, CancellationToken ct);

    Task<Result<Unit>> SignOutAllAsync(CancellationToken ct);

    Task<Result<Unit>> SetDefaultAsync(string accountKey, CancellationToken ct);

    Task<Result<Unit>> MoveAsync(string accountKey, int newIndex, CancellationToken ct);
}
