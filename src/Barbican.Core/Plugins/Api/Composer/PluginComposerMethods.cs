// Mirrors: lib/plugin-sandbox/host-api-composer.ts:107-143; host-api.ts:2067-2099.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>Production adapter construction seam; no method is registered until it is implemented.</summary>
public static class PluginComposerMethods
{
    public static IPluginApiMethod GetSnapshot(IPluginComposerAccess access) => throw new PortPendingException("composer.getSnapshot adapter");
    public static IPluginApiMethod SetSubject(IPluginComposerAccess access) => throw new PortPendingException("composer.setSubject adapter");
    public static IPluginApiMethod ReadAttachment(IPluginComposerAccess access) => throw new PortPendingException("composer.readAttachment adapter");
}
