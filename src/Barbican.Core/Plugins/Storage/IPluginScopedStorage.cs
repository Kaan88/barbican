// Source: Bulwark lib/plugin-sandbox/host-api.ts:407-447; trusted runtime/account captures and live grant fences are M6b additions.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Persistence;

/// <summary>Storage access through host-created runtime and captured-login identities.</summary>
public interface IPluginScopedStorage
{
    Task<Result<PluginValue?>> GetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, CancellationToken ct);
    Task<Result<Unit>> SetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, PluginValue value, CancellationToken ct);
    Task<Result<Unit>> RemoveAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, CancellationToken ct);
    Task<Result<IReadOnlyList<string>>> KeysAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, CancellationToken ct);
}
