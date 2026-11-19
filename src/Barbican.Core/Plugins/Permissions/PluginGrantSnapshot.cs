// Mirrors: Bulwark lib/plugin-types.ts:267-318 (installed/granted authority), plugin-sandbox/tier.ts:30-58.
// M6b-contracts: immutable package/account/generation binding. The broker must fetch the current snapshot on every use.
using System.Collections.Immutable;
using Barbican.Core.Plugins.Packages;

namespace Barbican.Core.Plugins.Permissions;

/// <summary>One host-issued grant version. It is not a substitute for live authority lookup or argument projection.</summary>
public sealed class PluginGrantSnapshot
{
    public PluginGrantSnapshot(PluginPackageIdentity package, long generation, bool enabled,
        IEnumerable<string> permissions, IEnumerable<string> accountKeys)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(accountKeys);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        Package = package;
        Generation = generation;
        Enabled = enabled;
        Permissions = permissions.ToImmutableHashSet(StringComparer.Ordinal);
        AccountKeys = accountKeys.ToImmutableHashSet(StringComparer.Ordinal);
        if (Permissions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Grant entries must be nonempty exact names.", nameof(permissions));
        if (AccountKeys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Grant entries must be nonempty exact names.", nameof(accountKeys));
    }

    public PluginPackageIdentity Package { get; }
    public long Generation { get; }
    public bool Enabled { get; }
    public ImmutableHashSet<string> Permissions { get; }
    public ImmutableHashSet<string> AccountKeys { get; }

    /// <summary>Checks a trusted transport binding against this snapshot. Null account is only for host-scoped methods.</summary>
    public bool Authorizes(PluginPackageIdentity package, long generation, string permission, string? accountKey) =>
        Enabled && Package == package && Generation == generation && Permissions.Contains(permission)
        && (accountKey is null || AccountKeys.Contains(accountKey));
}
