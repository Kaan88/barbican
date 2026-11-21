// Mirrors: Bulwark lib/phishing-report-flow.ts phishingSetupForEmail :34 (the policy of the account the message is reached through; no address, reporting off)
// and hooks/use-phishing-report.ts (follows the policy as it loads). The policy is the host-trusted organisation policy of the login (OrganizationPolicyService:
// an OAuth login with an approved provider); anything short of an authoritative answer is off. Owner reader.

using System.Collections.Concurrent;
using Barbican.Core.OrganizationPolicy;
using Barbican.Presentation.Reader;

namespace Barbican.App.Views.Reader;

/// <summary>The phishing report setup of each login, read from its organisation policy and kept for a short while for menus to ask without waiting.</summary>
internal sealed class OrganizationPhishingReportSetups(OrganizationPolicyService policy, TimeProvider time) : IPhishingReportSetups
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, (PhishingReportPolicy Value, DateTimeOffset At)> _known = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _reading = new(StringComparer.Ordinal);

    public PhishingReportPolicy Cached(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        if (_known.TryGetValue(accountKey, out var known) && time.GetUtcNow() - known.At < Fresh)
        {
            return known.Value;
        }

        // a menu cannot wait: it shows what is known and the answer is there the next time it opens
        if (_reading.TryAdd(accountKey, true))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ReadAsync(accountKey, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _reading.TryRemove(accountKey, out _);
                }
            });
        }

        return known.Value ?? PhishingReportPolicy.Off;
    }

    public async Task<PhishingReportPolicy> ReadAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        var lookup = await policy.ReadAsync(accountKey, ct).ConfigureAwait(false);
        var value = lookup.Snapshot?.Effective.PhishingReport ?? PhishingReportPolicy.Off;
        _known[accountKey] = (value, time.GetUtcNow());
        return value;
    }
}
