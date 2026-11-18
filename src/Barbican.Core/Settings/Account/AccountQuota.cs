// Mirrors: see IAccountQuota; Rust jmap.rs:1146 quota; Bulwark components/settings/account-settings.tsx (storage row). The quota object is picked by MailService.GetQuotaAsync (QuotaPicker, RFC 9425). G-057.

using Barbican.Core.Accounts;
using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>The default <see cref="IAccountQuota"/>.</summary>
public sealed class AccountQuota(IAccountManager accounts) : IAccountQuota
{
    /// <inheritdoc/>
    public Task<Result<AccountQuotaUsage>> UsageAsync(string accountKey, CancellationToken ct) => Task.Run(
        async () =>
        {
            var session = AccountCalls.Session(accounts, accountKey);
            if (!session.IsOk)
            {
                return Result<AccountQuotaUsage>.Fail(session.Error!);
            }

            var r = await session.Value!.Mail.GetQuotaAsync(ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<AccountQuotaUsage>.Fail(r.Error!);
            }

            // a server without a quota (or with a hard limit of 0) shows the usage only
            return Result<AccountQuotaUsage>.Ok(r.Value is { } q ? new AccountQuotaUsage(q.Used, q.HardLimit > 0 ? q.HardLimit : null) : new AccountQuotaUsage(0, null));
        },
        ct);
}
