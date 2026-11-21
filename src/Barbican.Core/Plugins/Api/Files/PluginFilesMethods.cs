// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:59-80 (files:read and files:write per method) and :681-693 (the dispatch). Typed adapters over the file store and the writer and transfer service the Files view uses; no raw JMAP
// is exposed. Not here: files.attach, files.saveAttachments (composer mediation, compose.open and a blob-to-file service Barbican lacks), upfiles.get and upfiles.save (a staged-upload store Barbican lacks).
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Files;

/// <summary>Factory for the files adapters.</summary>
public static class PluginFilesMethods
{
    /// <summary>The number of methods <see cref="Create"/> returns (four reads, four writes).</summary>
    public const int Count = 8;

    public static IReadOnlyList<IPluginApiMethod> Create(IPluginFilesHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return
        [
            new PluginFilesList(host), new PluginFilesFind(host), new PluginFilesGetText(host), new PluginFilesGetImage(host),
            new PluginFilesCreate(host), new PluginFilesCreateFolder(host), new PluginFilesMove(host), new PluginFilesRename(host),
        ];
    }
}
