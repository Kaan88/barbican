// Mirrors: Bulwark data slots. Data must pass the particular slot schema before registration, never generic arbitrary UI state.
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Slots;
public sealed class PluginDataContribution(PluginRuntimeBinding owner, string slot, PluginValue data)
{
    public PluginRuntimeBinding Owner { get; } = owner;
    public string Slot { get; } = slot;
    public PluginValue Data { get; } = data ?? throw new ArgumentNullException(nameof(data));
}
