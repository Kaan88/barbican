// Mirrors: Rust files.rs:233 create_in (FileNode/set create with onExists rename; @type on a draft-18 server), :381 node_set (the first SetError wins); Bulwark lib/jmap/client.ts createFileNode / usesDraftFileNodeCreate; RFC 8620 5.3 (/set);
// draft-ietf-jmap-filenode (FileNode/set: parentId, name, blobId, type or mediaType, shareWith); Stalwart crates/jmap/src/file/set.rs (onExists rename, onDestroyRemoveChildren). Owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Files;

/// <summary>The FileNode requests shared by the writer and the transfer.</summary>
internal static class FileJmap
{
    /// <summary>A create object in the server's shape: a folder (no blob) or a file.</summary>
    public static JsonObject NodeBody(bool draft, string? parentId, string name, string? blobId, string? mime)
    {
        if (blobId is not null)
        {
            return FilesTree.FileNode(draft, parentId, name, blobId, mime ?? "application/octet-stream");
        }

        return draft ? new JsonObject { ["@type"] = "Directory", ["parentId"] = parentId, ["name"] = name } : new JsonObject { ["parentId"] = parentId, ["name"] = name };
    }

    /// <summary>One FileNode/set; a method error is a failure, per-node SetErrors are in the outcome.</summary>
    public static async Task<Result<SetOutcome>> SetAsync(IJmapClient client, string account, JsonObject body, CancellationToken ct)
    {
        body["accountId"] = account;
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("FileNode/set", body, "s")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<SetOutcome>.Fail(r.Error!);
        }

        var args = r.Value!.Args("s", "FileNode/set");
        return args.IsOk ? Result<SetOutcome>.Ok(SetErrors.Read(args.Value)) : Result<SetOutcome>.Fail(args.Error!);
    }

    /// <summary>The outcome as a failure when the server refused any node (the first reason), else a success.</summary>
    public static Result<SetOutcome> Whole(Result<SetOutcome> r) => r.IsOk && r.Value!.Failed.Count > 0 ? Result<SetOutcome>.FailPartial(r.Value.Failed[0], r.Value) : r;

    /// <summary>Creates one node and returns its raw id; a name taken is made unique by the server (onExists rename).</summary>
    public static async Task<Result<string>> CreateAsync(IJmapClient client, bool draft, string account, string? parentId, string name, string? blobId, string? mime, CancellationToken ct)
    {
        var body = new JsonObject { ["onExists"] = "rename", ["create"] = new JsonObject { ["n"] = NodeBody(draft, parentId, name, blobId, mime) } };
        var set = Whole(await SetAsync(client, account, body, ct).ConfigureAwait(false));
        if (!set.IsOk)
        {
            return Result<string>.Fail(set.Error!);
        }

        return set.Value!.Created.TryGetValue("n", out var id) ? Result<string>.Ok(id) : Result<string>.Fail(new ValidationError("The server created nothing."));
    }

    /// <summary>The pointer-escaped key of a shareWith patch for one principal (RFC 6901).</summary>
    public static string ShareKey(string principalId) => "shareWith/" + principalId.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>Bulwark's read and readWrite presets in the account's wire shape (Rust files.rs:726 rights).</summary>
    public static JsonObject Rights(bool legacy, bool write) => legacy
        ? new JsonObject { ["mayRead"] = true, ["mayWrite"] = write, ["mayShare"] = false }
        : new JsonObject { ["mayRead"] = true, ["mayAddChildren"] = write, ["mayRename"] = write, ["mayDelete"] = write, ["mayModifyContent"] = write, ["mayShare"] = false };
}
