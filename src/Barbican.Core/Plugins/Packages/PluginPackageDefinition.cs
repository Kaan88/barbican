// Mirrors: Bulwark lib/plugin-types.ts:129-187 and extension-package.ts validated manifests.
// Immutable inert metadata; constructing it does not validate paths, signatures or execution authority.
using System.Collections.Immutable;
using System.Text.Json;
using Barbican.Core.Plugins.Permissions;
namespace Barbican.Core.Plugins.Packages;
public sealed class PluginPackageDefinition
{
    public PluginPackageDefinition(PluginPackageIdentity identity, PluginRuntimeKind runtimeKind, string entrypoint,
        JsonElement manifest, IEnumerable<string> requestedPermissions, PluginPackageSource source,
        PluginExecutionTier requestedTier = PluginExecutionTier.Untrusted)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrypoint);
        ArgumentNullException.ThrowIfNull(requestedPermissions);
        if (manifest.ValueKind != JsonValueKind.Object) throw new ArgumentException("A manifest object is required.", nameof(manifest));
        Identity = identity;
        RuntimeKind = runtimeKind;
        Entrypoint = entrypoint;
        Manifest = manifest.Clone();
        RequestedPermissions = requestedPermissions.ToImmutableHashSet(StringComparer.Ordinal);
        Source = source;
        RequestedTier = requestedTier;
    }
    public PluginPackageIdentity Identity { get; }
    public PluginRuntimeKind RuntimeKind { get; }
    public string Entrypoint { get; }
    public JsonElement Manifest { get; }
    public ImmutableHashSet<string> RequestedPermissions { get; }
    public PluginPackageSource Source { get; }
    public PluginExecutionTier RequestedTier { get; }
}
