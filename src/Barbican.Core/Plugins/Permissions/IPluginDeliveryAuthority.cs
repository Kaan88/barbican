using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Permissions;

/// <summary>Orders a synchronous native transport write against grant and profile changes.</summary>
public interface IPluginDeliveryAuthority : IPluginAuthority
{
    Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct);
}
