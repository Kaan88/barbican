// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:289-312 doFilesList and :49,681 (files:read): what a folder holds (folders first, then by name; at most 200); without a folder, the top of the user's drive and the top of what
// others shared with them. The Files trash and what is in it are never listed. A folder that is not there, or a file, is not a folder.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesList(IPluginFilesHost host) : PluginFilesMethod("files.list", "files:read", host, false)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.At(arguments, 0) is null or { Kind: PluginValueKind.Null } || PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        var folderKey = PluginMailArgs.At(arguments, 0) is { Kind: PluginValueKind.String } id ? id.StringValue : null;
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var nodes = PluginFilesData.Visible(snapshot);
        IEnumerable<Barbican.Core.Files.FileEntry> entries;
        if (folderKey is null)
        {
            var keys = nodes.Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
            // the top of what is shared: its parent is not something the user can open
            entries = nodes.Where(n => n.Shared ? n.ParentKey is null || !keys.Contains(n.ParentKey) : n.ParentKey is null);
        }
        else
        {
            if (PluginFilesData.Find(nodes, folderKey) is not { } folder) return Fail(PluginErrorCode.Unavailable);
            if (!folder.IsDirectory) return Fail(PluginErrorCode.InvalidArguments);
            entries = nodes.Where(n => string.Equals(n.ParentKey, folder.Key, StringComparison.Ordinal));
        }

        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. entries.OrderByDescending(n => n.IsDirectory).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Name, StringComparer.Ordinal)
            .Take(PluginFilesData.MaxListEntries).Select(n => PluginFileProjection.Info(b, snapshot.Entries, n))]));
    }
}
