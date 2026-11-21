// Mirrors: Bulwark host-api.ts permission/tier gate, api-timeouts.ts and the per-method input caps of host-api-input.ts:57-70 (each adapter's own readString limit).
// Conditional/nested checks belong to the typed adapter too.
using System.Collections.Immutable;
using Barbican.Core.Plugins.Protocol;
namespace Barbican.Core.Plugins.Bridge;
public sealed class PluginApiDescriptor
{
    public PluginApiDescriptor(string name, IEnumerable<IEnumerable<string>> permissionAlternatives, TimeSpan deadline,
        bool privilegedOnly = false, bool mayMutate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(permissionAlternatives);
        if (deadline <= TimeSpan.Zero && deadline != Timeout.InfiniteTimeSpan) throw new ArgumentOutOfRangeException(nameof(deadline));
        Name = name;
        PermissionAlternatives = permissionAlternatives.Select(x => x.ToImmutableArray()).ToImmutableArray();
        if (PermissionAlternatives.Any(x => x.IsEmpty || x.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Permission alternatives must be nonempty exact names.", nameof(permissionAlternatives));
        Deadline = deadline;
        PrivilegedOnly = privilegedOnly;
        MayMutate = mayMutate;
    }

    /// <summary>The library defaults: a method whose own validation accepts more declares its own budget.</summary>
    public static PluginValueLimits DefaultArgumentLimits { get; } = new();
    public string Name { get; }
    /// <summary>OR of AND groups; empty outer array is only for a reference implicit-permission method.</summary>
    public ImmutableArray<ImmutableArray<string>> PermissionAlternatives { get; }
    public TimeSpan Deadline { get; }
    public bool PrivilegedOnly { get; }
    public bool MayMutate { get; }
    /// <summary>Budgets for one call's decoded arguments, derived from the method's own input cap; null keeps <see cref="DefaultArgumentLimits"/>.</summary>
    public PluginValueLimits? ArgumentLimits { get; init; }
}
