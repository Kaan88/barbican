// Mirrors: lib/plugin-types.ts:1482-1487; host-api-composer.ts:124-143.
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>Detached copy of the exact bytes returned by the host capability.</summary>
public sealed class PluginComposerAttachmentData
{
    private readonly byte[] _data;
    public PluginComposerAttachmentData(string name, string type, ReadOnlyMemory<byte> data)
    {
        Name = name; Type = type; _data = data.ToArray();
    }
    public string Name { get; }
    public string Type { get; }
    public ReadOnlyMemory<byte> Data => _data.ToArray();
}
