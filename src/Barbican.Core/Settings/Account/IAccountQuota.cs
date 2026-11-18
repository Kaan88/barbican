// Mirrors: Rust jmap.rs quota, Core/Jmap/Quota/QuotaPicker.cs (M1 picks the octets quota for the sidebar ring); Bulwark components/settings/account-settings.tsx (storage usage row), G-057. Contract from the M6 plan section 4.5; owner set-data.


using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>The storage usage shown on the Account page. THREADING (D-M6-00): completes off the UI thread.</summary>
public interface IAccountQuota
{
    /// <summary>The octets quota of the account (Quota/get), or a failure; null Limit when the server sets none.</summary>
    Task<Result<AccountQuotaUsage>> UsageAsync(string accountKey, CancellationToken ct);
}
