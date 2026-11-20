// Mirrors: Bulwark lib/proofreading/gate.ts:42-65 and lib/admin/policy-scopes.ts:247-256. No Stalwart counterpart.
using System.Collections.Immutable;

namespace Barbican.Core.OrganizationPolicy;

/// <summary>Resolved defaults and locks for draft disclosure to proofreading add-ons.</summary>
public sealed record ProofreadingPolicy(ImmutableDictionary<string, bool> Defaults,
    ImmutableDictionary<string, ProofreadingRestriction> Restrictions)
{
    internal bool IsValid { get; init; } = true;

    public static ProofreadingPolicy Empty { get; } = new(ImmutableDictionary<string, bool>.Empty.WithComparers(StringComparer.Ordinal),
        ImmutableDictionary<string, ProofreadingRestriction>.Empty.WithComparers(StringComparer.Ordinal));

    public bool Allows(string pluginId, bool? userChoice, bool? perPluginChoice)
    {
        if (!IsValid) return false;
        const string setting = "proofreading";
        var perPlugin = setting + "." + pluginId;
        var mainLock = LockedValue(setting);
        var pluginLock = LockedValue(perPlugin);
        if (mainLock == false || pluginLock == false) return false;
        if (pluginLock == true || (mainLock == true && Restrictions.GetValueOrDefault(perPlugin)?.Locked != true)) return true;
        if (perPluginChoice is { } explicitPlugin) return explicitPlugin;
        if (Defaults.TryGetValue(perPlugin, out var pluginDefault)) return pluginDefault;
        return userChoice ?? Defaults.GetValueOrDefault(setting);
    }

    private bool? LockedValue(string key) => Restrictions.GetValueOrDefault(key) is { Locked: true } restriction
        ? restriction.Value ?? (Defaults.TryGetValue(key, out var fallback) ? fallback : null)
        : null;

    internal ProofreadingPolicy Apply(ProofreadingPolicy overlay) =>
        new(Defaults.SetItems(overlay.Defaults), Restrictions.SetItems(overlay.Restrictions)) { IsValid = IsValid && overlay.IsValid };

    internal bool SameRules(ProofreadingPolicy other) => IsValid == other.IsValid && Defaults.Count == other.Defaults.Count &&
        Restrictions.Count == other.Restrictions.Count && Defaults.All(pair => other.Defaults.TryGetValue(pair.Key, out var value) && pair.Value == value) &&
        Restrictions.All(pair => other.Restrictions.TryGetValue(pair.Key, out var value) && pair.Value == value);
}
