// Mirrors: Bulwark lib/files-save.ts saveBlobsToFiles (see IFileBlobSaver): per blob the onBeforeFileUpload intercept (a stop is listed, nothing is made), Blob/copy when the blob is in another account than the Files account,
// FileNode/set create with onExists rename (Rust files.rs:233 create_in), then onAfterFileUpload; the store is refreshed afterwards so the Files view shows the nodes. Owner files-data.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Files;

/// <summary>The default <see cref="IFileBlobSaver"/>.</summary>
public sealed class FileBlobSaver(IAccountManager accounts, IFileStore store, IHookBus hooks, TimeProvider? time = null) : IFileBlobSaver
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc/>
    public async Task<Result<FileBlobSaved>> SaveAsync(string accountKey, string? parentKey, string? blobAccountId, IReadOnlyList<FileBlobSource> blobs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(blobs);
        var opened = await FileContext.OpenAsync(accounts, store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk) return Result<FileBlobSaved>.Fail(opened.Error!);
        var ctx = opened.Value!;
        var target = ctx.Target(parentKey);
        if (!target.IsOk) return Result<FileBlobSaved>.Fail(target.Error!);
        var (account, parentId) = target.Value;
        var path = FilePaths.Of(ctx.Entries, parentKey) is { Length: > 0 } p ? p : "/";
        // the names in the folder, so a taken name becomes "name (2).ext" as for every other file the app makes (the server's onExists rename stays as the backstop)
        var used = ctx.Names(parentKey, account);
        var keys = new List<string>();
        var stopped = new List<string>();
        JmapError? failure = null;
        foreach (var blob in blobs)
        {
            var name = FileNameChecks.AcceptedName(string.IsNullOrEmpty(blob.Name) ? "attachment" : blob.Name, ctx.Snapshot.Rules);
            var type = string.IsNullOrEmpty(blob.MediaType) ? "application/octet-stream" : blob.MediaType;
            if (!await hooks.AllowAsync(HookNames.OnBeforeFileUpload, () => new FileUploadPayload(name, blob.Size, type, path), ct).ConfigureAwait(false))
            {
                stopped.Add(name);
                continue;
            }

            var blobId = await BlobInAsync(ctx, blobAccountId, account, blob.BlobId, ct).ConfigureAwait(false);
            if (!blobId.IsOk)
            {
                failure = blobId.Error;
                break;
            }

            // from here the node is made whole or not at all: this request is not cancelled half way
            var unique = used.Contains(name) ? FileNames.UniqueName(name, used) : name;
            _ = used.Add(unique);
            var made = await FileJmap.CreateAsync(ctx.Client, ctx.Draft, account, parentId, unique, blobId.Value, type, CancellationToken.None).ConfigureAwait(false);
            if (!made.IsOk)
            {
                failure = made.Error;
                break;
            }

            var key = FileEntry.MakeKey(account, made.Value!, !string.Equals(account, ctx.Primary, StringComparison.Ordinal));
            keys.Add(key);
            var uploaded = new FileResourceView(key, unique, "file", blob.Size, type, path, _time.GetUtcNow().ToString("o", CultureInfo.InvariantCulture));
            hooks.Fire(HookNames.OnAfterFileUpload, () => uploaded);
        }

        if (keys.Count > 0) _ = await store.RefreshAsync(accountKey, CancellationToken.None).ConfigureAwait(false);
        var saved = new FileBlobSaved(keys, stopped);
        return failure is null ? Result<FileBlobSaved>.Ok(saved) : Result<FileBlobSaved>.FailPartial(failure, saved);
    }

    // the blob's id in the Files account: its own, or the answer of Blob/copy when it lives in another account of the login
    private static async Task<Result<string>> BlobInAsync(FileContext ctx, string? blobAccountId, string filesAccount, string blobId, CancellationToken ct)
    {
        if (blobAccountId is null || string.Equals(blobAccountId, filesAccount, StringComparison.Ordinal)) return Result<string>.Ok(blobId);
        var args = new JsonObject { ["fromAccountId"] = blobAccountId, ["accountId"] = filesAccount, ["blobIds"] = new JsonArray(blobId) };
        var r = await ctx.Client.CallAsync(JmapRequest.Of(JmapRequest.Call("Blob/copy", args, "b")), ct).ConfigureAwait(false);
        var res = r.IsOk ? r.Value!.Args("b", "Blob/copy") : Result<JsonObject>.Fail(r.Error!);
        return res.IsOk ? BlobCopyRequests.Copied(res.Value!, blobId) : Result<string>.Fail(res.Error!);
    }
}
