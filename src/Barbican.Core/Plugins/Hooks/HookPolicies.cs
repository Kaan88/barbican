// Mirrors: spec/plugins/hooks.csv columns budget_ms and fail_closed (generated into HookPolicies.g.cs by tools/gen-hooks.py); Bulwark lib/plugin-hooks.ts:62-66
// (the defaults by kind for a name the catalogue does not know); fable-review-2 R2-10. Owner unified (M2 wave 2).

namespace Barbican.Core.Plugins.Hooks;

/// <summary>The budget and fail-closed flag of a hook, from the catalogue.</summary>
public static partial class HookPolicies
{
    /// <summary>The budget of an observer or transform hook the catalogue does not list (Bulwark DEFAULT_TIMEOUT_MS).</summary>
    public static readonly HookPolicy Default = new(TimeSpan.FromSeconds(5), false);

    /// <summary>The policy of a catalogued hook, or <see cref="Default"/> for a name outside the catalogue (tests, internal hooks).</summary>
    public static HookPolicy For(string hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        return Table.TryGetValue(hook, out var policy) ? policy : Default;
    }

    /// <summary>Whether the catalogue names this hook.</summary>
    public static bool IsCatalogued(string hook) => Table.ContainsKey(hook);
}
