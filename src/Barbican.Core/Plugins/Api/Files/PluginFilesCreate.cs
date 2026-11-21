// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:548-606 doFilesCreate (a new text or Markdown file of at most 512 KB in a folder of the user's own drive, never a shared one; a name that is taken becomes "name (2).md"; resolves to
// { file }) and :56,686 (files:write), d.types.ts:70-95. Goes through the transfer service the Files view uses (IFileTransfer.CreateTextFileAsync: onBeforeFileUpload runs, so another add-on can stop it; the store learns the node).
// Deviations: `replace` is not available (Barbican has no replace-content-with-a-trash-copy service; the call answers Unavailable), and the media type is the one of the name's extension (text/plain for an unknown one), not the
// `type` field, which only has to be text/markdown or text/plain.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesCreate(IPluginFilesHost host) : PluginFilesMethod("files.create", "files:write", host, true)
{
    private static readonly string[] Allowed = ["folderId", "name", "content", "type", "replace"];
    private static readonly string[] Types = ["text/markdown", "text/plain"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!TryParse(arguments, out var folderKey, out var name, out var content, out var replacing)) return Fail(PluginErrorCode.InvalidArguments);
        if (replacing) return Fail(PluginErrorCode.Unavailable);
        // counted before anything is read: a refused write counts too (d.ts:559)
        if (!CountWrite(call)) return Fail(PluginErrorCode.RateLimited);
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        var drive = new PluginFilesData.OwnDrive(snapshot);
        if (!PluginFileNames.AcceptedBy(name, drive.Rules)) return Fail(PluginErrorCode.InvalidArguments);
        var (folder, why) = drive.OwnFolder(folderKey);
        if (why != PluginFilesData.Refusal.None) return Fail(Refused(why));
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var made = await Host.Transfer.CreateTextFileAsync(call.AccountKey, folder?.Key, name, content, ct).ConfigureAwait(false);
        if (!made.IsOk) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        var fresh = Host.Store.Snapshot(call.AccountKey);
        var node = fresh is null ? null : PluginFilesData.Find(fresh.Entries, made.Value!);
        var info = node is null
            ? b.Object(("id", b.String(made.Value!)), ("name", b.String(name)), ("path", b.String(PluginFilesData.PathOfFolder(snapshot.Entries, folder))), ("parentId", folder is null ? b.Null() : b.String(folder.Key)),
                ("kind", b.String("file")), ("mimeType", b.String("text/plain")), ("size", b.Number(System.Text.Encoding.UTF8.GetByteCount(content))), ("modified", b.String("")))
            : PluginFileProjection.Info(b, fresh!.Entries, node);
        return Success(b, b.Object(("file", info)));
    }

    internal static PluginErrorCode Refused(PluginFilesData.Refusal why) => why switch
    {
        PluginFilesData.Refusal.Missing => PluginErrorCode.Unavailable,
        PluginFilesData.Refusal.NotFolder => PluginErrorCode.InvalidArguments,
        _ => PluginErrorCode.PermissionDenied,
    };

    private static bool TryParse(PluginValue arguments, out string? folderKey, out string name, out string content, out bool replacing)
    {
        (folderKey, name, content, replacing) = (null, "", "", false);
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Allowed, true, out var options)) return false;
        if (!PluginMailArgs.TryString(PluginMailArgs.Prop(options, "content"), PluginFilesData.MaxCreateChars, out content)) return false;
        if (PluginMailArgs.Prop(options, "type") is not { Kind: PluginValueKind.String } type || !Types.Contains(type.StringValue, StringComparer.Ordinal)) return false;
        if (PluginMailArgs.Prop(options, "replace") is { } replace)
        {
            if (!PluginMailArgs.IsId(replace)) return false;
            replacing = true;
        }

        if (PluginMailArgs.Prop(options, "folderId") is { Kind: not PluginValueKind.Null } folder)
        {
            if (!PluginMailArgs.IsId(folder)) return false;
            folderKey = folder.StringValue;
        }

        // with replace the file keeps its own name
        return replacing || (PluginMailArgs.TryString(PluginMailArgs.Prop(options, "name"), PluginFileNames.MaxNameChars, out var raw) && PluginFileNames.TryClean(raw, out name));
    }
}
