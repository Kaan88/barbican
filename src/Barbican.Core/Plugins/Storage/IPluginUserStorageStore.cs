// Source: Bulwark lib/plugin-sandbox/storage-scope.ts:17-23, 82-100; compare-and-commit is a host isolation addition.
using Barbican.Core.Common;

namespace Barbican.Core.Plugins.Persistence;

/// <summary>Atomic data snapshots keyed by host-selected profile, stable plugin id and local-login key.</summary>
/// <remarks>Implementations copy submitted bytes before any await and perform revision-checked durable commits. The service enforces the source UTF-16 quota before committing; serialized input is bounded by host policy.</remarks>
public interface IPluginUserStorageStore
{
    Task<Result<PluginStoreSnapshot>> LoadAsync(string profile, string pluginId, string localLoginKey, CancellationToken ct);
    Task<Result<long>> CommitAsync(string profile, string pluginId, string localLoginKey, long expectedRevision, ReadOnlyMemory<byte> data, CancellationToken ct);
}
