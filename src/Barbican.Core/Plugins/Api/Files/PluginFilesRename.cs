// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:657-675 doFilesRename and :59,689 (files:write): renames a file or folder of the user's own drive; the same name, or a name already used in that folder, refuses; resolves to
// { file, previousName } for an undo. The name goes through the server's rules (readNewName). Goes through the writer the Files view uses (IFileWriter.RenameAsync: onBeforeFileRename, onFileRename run; the store learns the change).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesRename(IPluginFilesHost host) : PluginFilesMethod("files.rename", "files:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!TryParse(arguments, out var fileKey, out var name)) return Fail(PluginErrorCode.InvalidArguments);
        // counted before anything is read: a refused write counts too (d.ts:661)
        if (!CountWrite(call)) return Fail(PluginErrorCode.RateLimited);
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        var drive = new PluginFilesData.OwnDrive(snapshot);
        if (!PluginFileNames.AcceptedBy(name, drive.Rules)) return Fail(PluginErrorCode.InvalidArguments);
        var (node, why) = drive.Own(fileKey);
        if (node is null) return Fail(PluginFilesCreate.Refused(why));
        if (string.Equals(name, node.Name, StringComparison.Ordinal)) return Fail(PluginErrorCode.Conflict);
        if (drive.Nodes.Any(n => string.Equals(n.ParentKey, node.ParentKey, StringComparison.Ordinal) && string.Equals(n.Name, name, StringComparison.Ordinal) && !string.Equals(n.Key, node.Key, StringComparison.Ordinal))) return Fail(PluginErrorCode.Conflict);
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var renamed = await Host.Writer.RenameAsync(call.AccountKey, node.Key, name, ct).ConfigureAwait(false);
        if (!renamed.IsOk) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        var fresh = Host.Store.Snapshot(call.AccountKey);
        var entries = fresh?.Entries ?? snapshot.Entries;
        var now = fresh is null ? null : PluginFilesData.Find(fresh.Entries, node.Key);
        var info = now is not null ? PluginFileProjection.Info(b, entries, now) : PluginFileProjection.Info(b, entries, node with { Name = name });
        return Success(b, b.Object(("file", info), ("previousName", b.String(node.Name))));
    }

    private static bool TryParse(PluginValue arguments, out string fileKey, out string name)
    {
        (fileKey, name) = ("", "");
        if (!PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0))) return false;
        fileKey = arguments.Item(0).StringValue;
        return PluginMailArgs.TryString(PluginMailArgs.At(arguments, 1), PluginFileNames.MaxNameChars, out var raw) && PluginFileNames.TryClean(raw, out name);
    }
}
