// Mirrors: Bulwark slot catalogue props/fields and permissions.ts filters; unknown host fields are omitted, not serialized transitively.
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Permissions;
namespace Barbican.Core.Plugins.Slots;
public interface IPluginSlotPolicy
{
    PluginSlotKind? KindOf(string slot);
    bool CanMount(string slot, PluginGrantSnapshot grant);
    Result<PluginValue> ProjectProps(string slot, PluginGrantSnapshot grant, PluginValue trustedHostProps);
}
