// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:59-80 (files:read and files:write per method) and :681-693 (the dispatch). Typed adapters over the file store and the writer and transfer service the Files view uses; no raw JMAP
// is exposed. files.saveAttachments is a separate factory (it also needs the mail readers and IFileBlobSaver). Not here: files.attach (composer mediation), upfiles.get and upfiles.save (a staged-upload store Barbican lacks).
using Barbican.Core.Files;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Files;

/// <summary>Factory for the files adapters.</summary>
public static class PluginFilesMethods
{
    /// <summary>The number of methods <see cref="Create"/> returns (four reads, four writes).</summary>
    public const int Count = 8;

    /// <summary>files.saveAttachments (files:write and email:read): files mail attachments by blob id into the user's own drive through <paramref name="saver"/>.</summary>
    public static IPluginApiMethod SaveAttachments(IPluginFilesHost host, IPluginMailReaders readers, IFileBlobSaver saver) => new PluginFilesSaveAttachments(host, readers, saver);

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
