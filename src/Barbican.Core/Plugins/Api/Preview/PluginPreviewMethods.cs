// Mirrors: lib/plugin-sandbox/host-api-file-viewer.ts:105-143; host-api-print.ts:49-67.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
namespace Barbican.Core.Plugins.Api.Preview;
/// <summary>Pending production preview adapter entrypoints.</summary>
public static class PluginPreviewMethods
{
    public static IPluginApiMethod OpenFile(IPluginFilePreview preview) => throw new PortPendingException("plugin file preview adapter");
    public static IPluginApiMethod Print(IPluginFilePreview preview, IPluginPrintPolicy policy) => throw new PortPendingException("plugin print adapter");
}
