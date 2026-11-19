// Mirrors: Bulwark host-api.ts permission/tier gate and api-timeouts.ts. Conditional/nested checks belong to the typed adapter too.
using System.Collections.Immutable;
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
    public string Name { get; }
    /// <summary>OR of AND groups; empty outer array is only for a reference implicit-permission method.</summary>
    public ImmutableArray<ImmutableArray<string>> PermissionAlternatives { get; }
    public TimeSpan Deadline { get; }
    public bool PrivilegedOnly { get; }
    public bool MayMutate { get; }
}
