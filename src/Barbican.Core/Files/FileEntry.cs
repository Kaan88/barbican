// Mirrors: Rust files.rs:1-2 header, :7-8 (node keys), :36 PROPS (13 properties), reader.rs:949 file_node; Bulwark components/files/file-browser.tsx, lib/jmap/ FileNode; draft-ietf-jmap-filenode in two shapes (Stalwart 0.16: nodeType directory or file, type; 1.0 / draft-18: @type, mediaType);
// Stalwart crates/jmap-proto/src/object/file_node.rs, crates/jmap/src/file/get.rs. B-FILE-001, B-FILE-003, B-FILE-004. Contract from the M5 plan section 4.6; owner files-data.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Files;

/// <summary>One file or folder of a login's file storage. <see cref="Key"/> is the bare id in the primary account and "account|id" in a shared one (B-FILE-003); <see cref="ParentKey"/> uses the same scheme.</summary>
public sealed record FileEntry(
    string Key,
    string AccountKey,
    bool Shared,
    string Id,
    string? ParentKey,
    string Name,
    bool IsDirectory,
    string? BlobId,
    long Size,
    DateTimeOffset? Modified,
    string? MediaType,
    JsonNode? ShareWith,
    JsonNode? MyRights = null)
{
    /// <summary>Reads a FileNode in either shape: a directory is nodeType "directory" or @type "Directory"; with neither, a node without a blobId (Rust files.rs:38 is_dir); the media type is <c>type</c> or <c>mediaType</c>; a node without id or name is a ValidationError. <paramref name="accountId"/> is the JMAP account the node came from (for the key).</summary>
    public static Result<FileEntry> FromJmap(JsonNode node, string accountKey, string accountId, bool shared)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(accountId);
        if (node is not JsonObject)
        {
            return Result<FileEntry>.Fail(new ValidationError("A file node is not an object."));
        }

        if (node.Str("id") is not { Length: > 0 } id)
        {
            return Result<FileEntry>.Fail(new ValidationError("A file node has no id."));
        }

        if (node.Str("name") is not { } name)
        {
            return Result<FileEntry>.Fail(new ValidationError("A file node has no name."));
        }

        var parent = node.Str("parentId") is { Length: > 0 } p ? MakeKey(accountId, p, shared) : null;
        var blobId = node.Str("blobId");
        long size = node["size"] is JsonValue v && v.TryGetValue<long>(out var l) ? Math.Max(0, l) : 0;
        DateTimeOffset? modified = node.Str("modified") is { } m && DateTimeOffset.TryParse(m, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        var media = node.Str("type") ?? node.Str("mediaType");
        return Result<FileEntry>.Ok(new FileEntry(
            MakeKey(accountId, id, shared), accountKey, shared, id, parent, name, IsDirectoryNode(node), blobId, size, modified, media,
            node["shareWith"] is { } sw && sw is not JsonValue ? sw.DeepClone() : null,
            node["myRights"] is JsonObject r ? r.DeepClone() : null));
    }

    /// <summary>The key of an id in an account: the bare id for the primary account, "account|id" for a shared one.</summary>
    public static string MakeKey(string accountId, string id, bool shared) => shared ? accountId + "|" + id : id;

    /// <summary>The JMAP account id and raw node id of a key; the account id is <paramref name="primaryAccountId"/> for a bare id.</summary>
    public static (string AccountId, string Id) SplitKey(string key, string primaryAccountId)
    {
        ArgumentNullException.ThrowIfNull(key);
        var bar = key.IndexOf('|', StringComparison.Ordinal);
        return bar < 0 ? (primaryAccountId, key) : (key[..bar], key[(bar + 1)..]);
    }

    // Rust files.rs:38 is_dir: nodeType, else @type (Directory / File), else no blob
    private static bool IsDirectoryNode(JsonNode node)
    {
        var kind = node.Str("nodeType") ?? node.Str("@type") switch
        {
            null => null,
            "Directory" => "directory",
            "File" => "file",
            _ => "other",
        };
        return kind is null ? node["blobId"] is null : string.Equals(kind, "directory", StringComparison.Ordinal);
    }
}
