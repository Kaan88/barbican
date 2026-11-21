// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:629-655 doFilesMove and :58,688 (files:write): moves a file or folder within the user's own drive (null: to the top), never into or out of a shared folder, the trash or another
// account; a folder is never moved into itself; a name that is already used in the target refuses; resolves to { file, previousFolderId } for an undo. Goes through the writer the Files view uses (IFileWriter.MoveAsync:
// onBeforeFileMove, onFileMove run; the store learns the change).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesMove(IPluginFilesHost host) : PluginFilesMethod("files.move", "files:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!TryParse(arguments, out var fileKey, out var folderKey)) return Fail(PluginErrorCode.InvalidArguments);
        // a folder of another person's drive (an id with an account in it) is never a target (d.ts:634)
        if (folderKey is not null && folderKey.Contains('|', StringComparison.Ordinal)) return Fail(PluginErrorCode.PermissionDenied);
        // counted before anything is read: a refused write counts too (d.ts:636)
        if (!CountWrite(call)) return Fail(PluginErrorCode.RateLimited);
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        var drive = new PluginFilesData.OwnDrive(snapshot);
        var (node, why) = drive.Own(fileKey);
        if (node is null) return Fail(PluginFilesCreate.Refused(why));
        var (target, targetWhy) = drive.OwnFolder(folderKey);
        if (targetWhy != PluginFilesData.Refusal.None) return Fail(PluginFilesCreate.Refused(targetWhy));
        var from = node.ParentKey;
        if (string.Equals(target?.Key, from, StringComparison.Ordinal)) return Fail(PluginErrorCode.Conflict);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var at = target; at is not null && seen.Add(at.Key); at = at.ParentKey is null ? null : drive.ById.GetValueOrDefault(at.ParentKey))
        {
            if (string.Equals(at.Key, node.Key, StringComparison.Ordinal)) return Fail(PluginErrorCode.InvalidArguments);
        }

        if (drive.Nodes.Any(n => string.Equals(n.ParentKey, target?.Key, StringComparison.Ordinal) && string.Equals(n.Name, node.Name, StringComparison.Ordinal) && !string.Equals(n.Key, node.Key, StringComparison.Ordinal))) return Fail(PluginErrorCode.Conflict);
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var moved = await Host.Writer.MoveAsync(call.AccountKey, [node.Key], target?.Key, ct).ConfigureAwait(false);
        if (!moved.IsOk) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        var fresh = Host.Store.Snapshot(call.AccountKey);
        var entries = fresh?.Entries ?? snapshot.Entries;
        var now = fresh is null ? null : PluginFilesData.Find(fresh.Entries, node.Key);
        var info = now is not null ? PluginFileProjection.Info(b, entries, now) : PluginFileProjection.Info(b, entries, node with { ParentKey = target?.Key });
        return Success(b, b.Object(("file", info), ("previousFolderId", from is null ? b.Null() : b.String(from))));
    }

    private static bool TryParse(PluginValue arguments, out string fileKey, out string? folderKey)
    {
        (fileKey, folderKey) = ("", null);
        if (!PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0))) return false;
        fileKey = arguments.Item(0).StringValue;
        if (PluginMailArgs.At(arguments, 1) is not { Kind: not PluginValueKind.Null } folder) return true;
        if (!PluginMailArgs.IsId(folder)) return false;
        folderKey = folder.StringValue;
        return true;
    }
}
