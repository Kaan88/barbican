// Mirrors: Bulwark data slots. Data must pass the particular slot schema before registration, never generic arbitrary UI state.
using System.Text.Json;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Slots;
public sealed class PluginDataContribution(PluginRuntimeBinding owner, string slot, JsonElement data)
{
    public PluginRuntimeBinding Owner { get; } = owner;
    public string Slot { get; } = slot;
    public JsonElement Data { get; } = data.Clone();
}
