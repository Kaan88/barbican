// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:608-627 doFilesCreateFolder (a new folder in a folder of the user's own drive, never a shared one; a name that is taken becomes "name (2)"; resolves to the folder) and :57,687
// (files:write). Goes through the writer the Files view uses (IFileWriter.CreateFolderAsync: onDirectoryCreate runs; the store learns the node). Deviation: the server's folder depth limit is not checked here (the server answers).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesCreateFolder(IPluginFilesHost host) : PluginFilesMethod("files.createFolder", "files:write", host, true)
{
    private static readonly string[] Allowed = ["parentId", "name"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!TryParse(arguments, out var parentKey, out var name)) return Fail(PluginErrorCode.InvalidArguments);
        // counted before anything is read: a refused write counts too (d.ts:612)
        if (!CountWrite(call)) return Fail(PluginErrorCode.RateLimited);
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        var drive = new PluginFilesData.OwnDrive(snapshot);
        if (!PluginFileNames.AcceptedBy(name, drive.Rules)) return Fail(PluginErrorCode.InvalidArguments);
        var (parent, why) = drive.OwnFolder(parentKey);
        if (why != PluginFilesData.Refusal.None) return Fail(PluginFilesCreate.Refused(why));
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var made = await Host.Writer.CreateFolderAsync(call.AccountKey, parent?.Key, name, ct).ConfigureAwait(false);
        if (!made.IsOk) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        var fresh = Host.Store.Snapshot(call.AccountKey);
        if (fresh is not null && PluginFilesData.Find(fresh.Entries, made.Value!) is { } node) return Success(b, PluginFileProjection.Info(b, fresh.Entries, node));
        return Success(b, b.Object(("id", b.String(made.Value!)), ("name", b.String(name)), ("path", b.String(PluginFilesData.PathOfFolder(snapshot.Entries, parent))),
            ("parentId", parent is null ? b.Null() : b.String(parent.Key)), ("kind", b.String("folder")), ("mimeType", b.String("")), ("size", b.Number(0)), ("modified", b.String(""))));
    }

    private static bool TryParse(PluginValue arguments, out string? parentKey, out string name)
    {
        (parentKey, name) = (null, "");
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Allowed, true, out var options)) return false;
        if (PluginMailArgs.Prop(options, "parentId") is { Kind: not PluginValueKind.Null } parent)
        {
            if (!PluginMailArgs.IsId(parent)) return false;
            parentKey = parent.StringValue;
        }

        return PluginMailArgs.TryString(PluginMailArgs.Prop(options, "name"), PluginFileNames.MaxNameChars, out var raw) && PluginFileNames.TryClean(raw, out name);
    }
}
