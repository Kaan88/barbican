// Mirrors: Bulwark lib/plugin-types.ts:267-318 (installed/granted authority), plugin-sandbox/tier.ts:30-58.
// M6b-contracts: immutable package/account/generation binding. The broker must fetch the current snapshot on every use.
using System.Collections.Immutable;
using Barbican.Core.Plugins.Packages;

namespace Barbican.Core.Plugins.Permissions;

/// <summary>One host-issued grant version. It is not a substitute for live authority lookup or argument projection.</summary>
public sealed class PluginGrantSnapshot
{
    public PluginGrantSnapshot(PluginPackageIdentity package, long generation, bool enabled,
        IEnumerable<string> permissions, IEnumerable<string> accountKeys, PluginExecutionTier tier = PluginExecutionTier.Untrusted)
        : this(package, generation, enabled, permissions, accountKeys, null, tier)
    {
    }

    public PluginGrantSnapshot(PluginPackageIdentity package, long generation, bool enabled,
        IEnumerable<string> permissions, IEnumerable<string> accountKeys, IEnumerable<string>? approvedHttpOrigins,
        PluginExecutionTier tier = PluginExecutionTier.Untrusted)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(accountKeys);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        Package = package;
        Generation = generation;
        if (!Enum.IsDefined(tier)) throw new ArgumentOutOfRangeException(nameof(tier));
        Tier = tier;
        Enabled = enabled;
        Permissions = permissions.ToImmutableHashSet(StringComparer.Ordinal);
        AccountKeys = accountKeys.ToImmutableHashSet(StringComparer.Ordinal);
        ApprovedHttpOrigins = (approvedHttpOrigins ?? []).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        if (Permissions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Grant entries must be nonempty exact names.", nameof(permissions));
        if (AccountKeys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Grant entries must be nonempty exact names.", nameof(accountKeys));
        if (ApprovedHttpOrigins.Any(string.IsNullOrWhiteSpace) || (!ApprovedHttpOrigins.IsEmpty && !Permissions.Contains("http:fetch")))
            throw new ArgumentException("HTTP origins require the HTTP permission.", nameof(approvedHttpOrigins));
    }

    public PluginPackageIdentity Package { get; }
    public long Generation { get; }
    public bool Enabled { get; }
    public PluginExecutionTier Tier { get; }
    public ImmutableHashSet<string> Permissions { get; }
    /// <summary>Authorized local-login keys; shared JMAP resource-account IDs require separate captured-session validation.</summary>
    public ImmutableHashSet<string> AccountKeys { get; }
    /// <summary>HTTPS origins reviewed for this exact package. Legacy approvals without a reviewed list have no HTTP origins.</summary>
    public ImmutableHashSet<string> ApprovedHttpOrigins { get; }

    /// <summary>Checks a trusted transport binding against this snapshot. Null account is only for host-scoped methods.</summary>
    public bool Authorizes(PluginPackageIdentity package, long generation, string permission, string? accountKey) =>
        Enabled && Package == package && Generation == generation && Permissions.Contains(permission)
        && (accountKey is null || AccountKeys.Contains(accountKey));
}
