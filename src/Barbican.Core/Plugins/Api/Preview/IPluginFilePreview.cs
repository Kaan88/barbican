// Mirrors: lib/plugin-sandbox/host-api-file-viewer.ts:105-143; host-api-print.ts:49-67.
using Barbican.Core.Plugins.Bridge;
namespace Barbican.Core.Plugins.Api.Preview;
/// <summary>Host-owned in-window preview. After UI dispatch, the host must revalidate the invocation context immediately before displaying, and for PrintPdf recheck the current aggregate policy over all signed-in accounts at the actual issue point. Return true only after acceptance.</summary>
public interface IPluginFilePreview
{
    Task<bool> ShowAsync(PluginPreviewFile file, PluginPreviewPurpose purpose, IPluginInvocationContext context, CancellationToken ct);
}
