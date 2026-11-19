// Mirrors: lib/plugin-sandbox/host-api-file-viewer.ts:19-103.
namespace Barbican.Core.Plugins.Api.Preview;
/// <summary>Detached file passed to the host-owned in-window preview.</summary>
public sealed class PluginPreviewFile
{
    private readonly byte[] _bytes;
    public PluginPreviewFile(string name, string contentType, ReadOnlyMemory<byte> bytes) { Name = name; ContentType = contentType; _bytes = bytes.ToArray(); }
    public string Name { get; }
    public string ContentType { get; }
    public ReadOnlyMemory<byte> Bytes => _bytes.ToArray();
}
