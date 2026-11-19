// Source: Bulwark lib/plugin-sandbox/host-api.ts:407-447 and storage-scope.ts:17-23; live binding/capture revalidation is an M6b addition.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Persistence;

/// <summary>Plugin user storage over a host-created runtime binding and captured local login. Test-first stub.</summary>
public sealed class PluginScopedStorage : IPluginScopedStorage
{
    public PluginScopedStorage(string profile, IPluginAuthority authority, IPluginSessionSource sessions, IPluginUserStorageStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(store);
    }

    public Task<Result<PluginValue?>> GetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<Unit>> SetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, PluginValue value, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<Unit>> RemoveAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<IReadOnlyList<string>>> KeysAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
}
