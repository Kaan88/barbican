// Mirrors Bulwark lib/device-policy.ts: ui.print uses the strictest policy of connected accounts.
using Barbican.Core.Accounts;

namespace Barbican.Core.OrganizationPolicy;

/// <summary>Resolves the device policy for printing documents without a single account owner.</summary>
public sealed class OrganizationPrintPolicy(IAccountOverview overview, IAccountSessionSnapshots accounts, OrganizationPolicyService policy)
{
    public async Task<Authorization?> ReadAsync(CancellationToken ct)
    {
        var active = accounts.CaptureActiveSession().Value;
        if (active is null) return null;
        var activeKey = active.Session.Account.Key;
        var keys = ConnectedKeys(activeKey);
        var captures = new List<AccountSessionSnapshot>(keys.Length);
        foreach (var key in keys)
        {
            var captured = string.Equals(key, activeKey, StringComparison.Ordinal) ? active : accounts.CaptureOwnerSession(key).Value;
            if (captured is null || !string.Equals(captured.Session.Account.Key, key, StringComparison.Ordinal)) return null;
            captures.Add(captured);
        }

        var policies = await Task.WhenAll(captures.Select(capture => policy.ReadForSessionAsync(capture, ct))).ConfigureAwait(false);
        if (policies.Any(lookup => lookup.Snapshot is not { Effective.NoPrint: false })) return null;
        var snapshots = policies.Select(lookup => lookup.Snapshot!).ToArray();
        var watermark = snapshots.Any(snapshot => snapshot.Effective.Watermark)
            ? snapshots.Single(snapshot => string.Equals(snapshot.LocalAccountKey, activeKey, StringComparison.Ordinal)).AccountAddress : null;
        var result = new Authorization(watermark, issue => RunPolicies(snapshots, 0, () =>
            keys.SequenceEqual(ConnectedKeys(activeKey), StringComparer.Ordinal) && issue()));
        return result.TryRunWhileCurrent(static () => true) ? result : null;
    }

    private string[] ConnectedKeys(string activeKey) =>
        [.. overview.Statuses.Where(status => status.IsConnected).Select(status => status.Account.Key)
            .Append(activeKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static bool RunPolicies(OrganizationPolicySnapshot[] snapshots, int index, Func<bool> issue) =>
        index == snapshots.Length ? issue() :
        snapshots[index].TryRunWhileCurrent(() => RunPolicies(snapshots, index + 1, issue));

    /// <summary>All connected accounts currently allow printing under the captured sessions.</summary>
    public sealed class Authorization
    {
        private readonly Func<Func<bool>, bool> _whileCurrent;

        internal Authorization(string? watermarkAddress, Func<Func<bool>, bool> whileCurrent)
        {
            WatermarkAddress = watermarkAddress;
            _whileCurrent = whileCurrent;
        }

        public string? WatermarkAddress { get; }

        public bool TryRunWhileCurrent(Func<bool> issue)
        {
            ArgumentNullException.ThrowIfNull(issue);
            return _whileCurrent(issue);
        }
    }
}
