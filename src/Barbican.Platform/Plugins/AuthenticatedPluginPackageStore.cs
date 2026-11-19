// Source: Bulwark stores/plugin-store.ts:188-320, 467-516; authenticated durable profile storage is host-added.
using System.Collections.Immutable;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Storage;
namespace Barbican.Platform.Plugins;

/// <summary>Host-authenticated package index and hash-addressed archive storage.</summary>
public sealed class AuthenticatedPluginPackageStore : IPluginPackageStore, IDisposable
{
    private AuthenticatedPluginPackageStore() { }
    public static Task<Result<AuthenticatedPluginPackageStore>> OpenAsync(IProfilePaths paths, IPluginProfileAuthorityLease lease, ISecretStore secrets, CancellationToken ct) => throw new PortPendingException("M6b authenticated package store");
    public Task<Result<PluginStoreSnapshot>> LoadAsync(string profile, CancellationToken ct) => throw new PortPendingException("M6b authenticated package store");
    public Task<Result<long>> CommitAsync(string profile, long expectedRevision, ReadOnlyMemory<byte> index, CancellationToken ct) => throw new PortPendingException("M6b authenticated package store");
    public Task<Result<Unit>> PutContentAsync(string profile, PluginPackageIdentity identity, ReadOnlyMemory<byte> archive, CancellationToken ct) => throw new PortPendingException("M6b authenticated package store");
    public Task<Result<ImmutableArray<byte>>> ReadContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct) => throw new PortPendingException("M6b authenticated package store");
    public Task<Result<Unit>> DeleteContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct) => throw new PortPendingException("M6b authenticated package store");
    public void Dispose() { }
}
