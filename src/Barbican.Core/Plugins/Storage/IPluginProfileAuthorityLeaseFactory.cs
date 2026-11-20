// Host-added M6b profile ownership; Bulwark stores/plugin-store.ts:188-320 assumes a single browser authority.
using Barbican.Core.Common;
using Barbican.Core.Storage;
namespace Barbican.Core.Plugins.Persistence;

/// <summary>Host-selected profile roots only; package metadata never chooses a lease path.</summary>
public interface IPluginProfileAuthorityLeaseFactory
{
    Task<Result<IPluginProfileAuthorityLease>> AcquireAsync(IProfilePaths paths, CancellationToken ct);
}
