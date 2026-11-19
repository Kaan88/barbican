// Mirrors: lib/plugin-sandbox/host-api-file-viewer.ts:105-143.
namespace Barbican.Core.Plugins.Api.Preview;
/// <summary>Host presentation capability; true means the in-window preview accepted the file.</summary>
public interface IPluginFilePreview
{
    Task<bool> ShowAsync(PluginPreviewFile file, PluginPreviewPurpose purpose, CancellationToken ct);
}
