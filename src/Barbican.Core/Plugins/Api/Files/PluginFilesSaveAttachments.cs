// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:506-546 doFilesSaveAttachments (files:write and email:read, host-api: d.ts:55,685; emailId, 1 to 20 attachment blob ids, an optional folder of the user's own drive, options.accountId;
// a name that is taken becomes "name (2).ext"; resolves to { saved: FileInfo[], stopped: string[] }), lib/files-save.ts saveBlobsToFiles (by blobId, no download or upload; Blob/copy from a shared mailbox; onBeforeFileUpload
// may stop a file, onAfterFileUpload reports each) and d.ts:73,151-155 (20 attachments at once, writes counted before anything is read). Goes through IFileBlobSaver, so the Files view shows the nodes.
// Deviations: a message, blob or account this login cannot read is Unavailable (Bulwark names it); a blob that is not an attachment of the message is InvalidArguments; the account must be one the captured login shows (Bulwark's
// readNamedEmail checks the same list); a failure after some files were saved is a plain Failed (the files saved before it stay, as in Bulwark).
using Barbican.Core.Files;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesSaveAttachments(IPluginFilesHost host, IPluginMailReaders readers, IFileBlobSaver saver) : PluginFilesMethod("files.saveAttachments", "files:write", host, true)
{
    /// <summary>Most attachments one call takes (d.ts:73).</summary>
    internal const int MaxAtOnce = 20;

    private readonly IPluginMailReaders _readers = readers ?? throw new ArgumentNullException(nameof(readers));
    private readonly IFileBlobSaver _saver = saver ?? throw new ArgumentNullException(nameof(saver));
    private static readonly string[] Allowed = ["accountId"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!TryParse(arguments, out var request)) return Fail(PluginErrorCode.InvalidArguments);
        // the second permission (d.ts:512 needPermission): the grant must name this login for email:read too
        if (!call.Context.Demand("email:read", call.AccountKey).IsOk) return Fail(PluginErrorCode.PermissionDenied);
        // counted before anything is read: a refused write counts too (d.ts:520)
        if (!CountWrite(call)) return Fail(PluginErrorCode.RateLimited);
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        var drive = new PluginFilesData.OwnDrive(snapshot);
        var (folder, why) = drive.OwnFolder(request.FolderKey);
        if (why != PluginFilesData.Refusal.None) return Fail(PluginFilesCreate.Refused(why));
        var route = PluginMailRouting.Resolve(call.Session, request.AccountId);
        if (!route.Allowed) return Fail(PluginErrorCode.Unavailable);
        var found = await _readers.Reader(call.Session, route.SharedAccountId).GetBodyAsync(request.EmailId, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        if (!found.IsOk) return Fail(PluginErrorCode.Unavailable);
        var sources = new List<FileBlobSource>();
        foreach (var blobId in request.BlobIds)
        {
            var part = found.Value!.Attachments.FirstOrDefault(a => string.Equals(a.BlobId, blobId, StringComparison.Ordinal));
            // "that blob is not an attachment of this message": a plugin cannot file an arbitrary blob
            if (part is null) return Fail(PluginErrorCode.InvalidArguments);
            sources.Add(new FileBlobSource(blobId, part.Name ?? "", part.Type ?? "", part.Size));
        }

        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var saved = await _saver.SaveAsync(call.AccountKey, folder?.Key, route.AccountId, sources, ct).ConfigureAwait(false);
        // the add-on, its grant or the login may have changed while the files were made: they exist, the answer is withheld (an unknown outcome, never a stale-context "nothing happened")
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.UnknownOutcome);
        if (!saved.IsOk) return Fail(PluginErrorCode.Failed);
        return Project(call, saved.Value!);
    }

    private PluginCallOutcome Project(PluginFilesCall call, FileBlobSaved saved)
    {
        var fresh = Host.Store.Snapshot(call.AccountKey);
        var b = new PluginValueBuilder();
        var infos = new List<int>();
        foreach (var key in saved.Keys)
        {
            if (fresh is not null && PluginFilesData.Find(fresh.Entries, key) is { } node) infos.Add(PluginFileProjection.Info(b, fresh.Entries, node));
        }

        return Success(b, b.Object(("saved", b.Array([.. infos])), ("stopped", b.Array([.. saved.Stopped.Select(s => b.String(s))]))));
    }

    private static bool TryParse(PluginValue arguments, out Request request)
    {
        request = default!;
        if (!PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0))) return false;
        if (PluginMailArgs.At(arguments, 1) is not { Kind: PluginValueKind.Array } list || list.Count is 0 or > MaxAtOnce) return false;
        var ids = new List<string>();
        for (var i = 0; i < list.Count; i++)
        {
            if (!PluginMailArgs.IsId(list.Item(i))) return false;
            if (!ids.Contains(list.Item(i).StringValue, StringComparer.Ordinal)) ids.Add(list.Item(i).StringValue);
        }

        string? folder = null;
        if (PluginMailArgs.At(arguments, 2) is { Kind: not PluginValueKind.Null } folderArg)
        {
            if (!PluginMailArgs.IsId(folderArg)) return false;
            folder = folderArg.StringValue;
        }

        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 3), Allowed, false, out var options)) return false;
        if (!PluginMailArgs.TryAccount(PluginMailArgs.Prop(options, "accountId"), out var account)) return false;
        request = new Request(arguments.Item(0).StringValue, ids, folder, account);
        return true;
    }

    private sealed record Request(string EmailId, IReadOnlyList<string> BlobIds, string? FolderKey, string? AccountId);
}
