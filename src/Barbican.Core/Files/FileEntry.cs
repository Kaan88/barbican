// Mirrors: Rust files.rs:1-2 header, :7-8 (node keys), :36 PROPS (13 properties), reader.rs:949 file_node; Bulwark components/files/file-browser.tsx, lib/jmap/ FileNode; draft-ietf-jmap-filenode in two shapes (Stalwart 0.16: nodeType directory or file, type; 1.0 / draft-18: @type, mediaType);
// Stalwart crates/jmap-proto/src/object/file_node.rs, crates/jmap/src/file/get.rs. B-FILE-001, B-FILE-003, B-FILE-004. Contract from the M5 plan section 4.6; owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

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
    JsonNode? ShareWith)
{
    /// <summary>Reads a FileNode in either shape: a directory is nodeType "directory" or @type "Directory"; with neither, a node without a blobId (Rust files.rs:38 is_dir); the media type is <c>type</c> or <c>mediaType</c>; a node without id or name is a ValidationError. <paramref name="accountId"/> is the JMAP account the node came from (for the key).</summary>
    public static Result<FileEntry> FromJmap(JsonNode node, string accountKey, string accountId, bool shared) => throw new PortPendingException("FileEntry.FromJmap");

    /// <summary>The key of an id in an account: the bare id for the primary account, "account|id" for a shared one.</summary>
    public static string MakeKey(string accountId, string id, bool shared) => throw new PortPendingException("FileEntry.MakeKey");
}
