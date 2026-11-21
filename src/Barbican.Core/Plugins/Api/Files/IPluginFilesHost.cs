// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:1-15 (the user's Files: over the JMAP client and the Files helpers, never the Files store's actions; the hooks the store fires fire here too) and :61-80 (limits).
// Barbican runs the calls on the file store the Files view shows and the one writer and transfer service it uses (IFileWriter, IFileTransfer: onBeforeFileUpload, onDirectoryCreate, onBeforeFileMove, onBeforeFileRename and the
// observers run, the open view updates). Draft-ietf-jmap-filenode; Stalwart crates/jmap/src/file/{get,set}.rs.
using Barbican.Core.Files;
using Barbican.Core.Plugins.Api.Mail;

namespace Barbican.Core.Plugins.Api.Files;

/// <summary>The file services the add-on files methods run on.</summary>
public interface IPluginFilesHost
{
    IFileStore Store { get; }

    IFileWriter Writer { get; }

    IFileTransfer Transfer { get; }

    /// <summary>The per-add-on minute budgets (reads of file content, changes to Files; d.ts:71-74).</summary>
    IPluginMailRateLimiter Limiter { get; }
}
