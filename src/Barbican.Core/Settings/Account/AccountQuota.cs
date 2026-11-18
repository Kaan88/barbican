// Mirrors: see IAccountQuota. Stub from the M6 plan section 4.5; owner set-data replaces the class (constructor dependencies: IAccountManager, QuotaPicker).

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>The default <see cref="IAccountQuota"/>.</summary>
public sealed class AccountQuota : IAccountQuota
{
    public Task<Result<AccountQuotaUsage>> UsageAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AccountQuota.UsageAsync");
}
