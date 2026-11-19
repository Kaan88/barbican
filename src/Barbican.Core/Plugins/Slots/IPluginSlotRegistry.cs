// Mirrors: Bulwark frame/action/data stores and replacement/disposal. Every implementation must revalidate live grants and the slot-specific schema.
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Slots;
public interface IPluginSlotRegistry
{
    event EventHandler? Changed;
    Result<Unit> RegisterFrames(PluginRuntimeBinding owner, IReadOnlyList<PluginSlotRegistration> registrations);
    Result<Unit> SetActions(PluginRuntimeBinding owner, string slot, IReadOnlyList<PluginAction> actions);
    Result<Unit> SetData(PluginRuntimeBinding owner, string slot, PluginValue data);
    IReadOnlyList<PluginFrameContribution> Frames(string slot);
    IReadOnlyList<PluginActionContribution> Actions(string slot);
    IReadOnlyList<PluginDataContribution> Data(string slot);
    void Remove(PluginRuntimeBinding owner);
}
