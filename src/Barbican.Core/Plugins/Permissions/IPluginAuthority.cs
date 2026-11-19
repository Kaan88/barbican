// Mirrors: Bulwark lib/plugin-sandbox/permissions.ts; callers fetch current authority rather than trusting a retained snapshot.
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Permissions;
public interface IPluginAuthority
{
    event EventHandler<PluginAuthorityChangedEventArgs>? Changed;
    Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct);
}
