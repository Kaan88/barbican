// Source: Bulwark stores/plugin-store.ts:188-320, 467-516; host-authenticated atomic storage is an M6b addition.
using System.Collections.Immutable;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;

namespace Barbican.Core.Plugins.Persistence;

/// <summary>Trusted host persistence. Implementations authenticate the index with a host-held secret; a disk flag is not proof.</summary>
/// <remarks>Each profile is host-selected. Commit implementations copy the supplied bytes before any await, compare revision, durably commit and read back. Index limit is 4 MiB; one archive is at most 5 MiB. Content is immutable and indexed by exact identity; callers rehash it. A failed or ambiguous commit does not establish new authority.</remarks>
public interface IPluginPackageStore
{
    Task<Result<PluginStoreSnapshot>> LoadAsync(string profile, CancellationToken ct);
    Task<Result<long>> CommitAsync(string profile, long expectedRevision, ReadOnlyMemory<byte> index, CancellationToken ct);
    Task<Result<Unit>> PutContentAsync(string profile, PluginPackageIdentity identity, ReadOnlyMemory<byte> archive, CancellationToken ct);
    Task<Result<ImmutableArray<byte>>> ReadContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct);
    Task<Result<Unit>> DeleteContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct);
}
