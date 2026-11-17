// Mirrors: Rust reader.rs:728 is_message_attachment, :872 is_dir, :887 folder_tree, :949 file_node; JMAP FileNode (draft-ietf-jmap-filenode:
// @type File/Directory, mediaType) and Stalwart 0.16 (nodeType, type), crates/jmap/src/file/{get,query,set}.rs; Bulwark
// components/email/save-to-files-dialog.tsx, lib/files-save.ts, components/email/reader-attachments.tsx.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Mail.Reader;

/// <summary>FileNode helpers for Save to Files and the message-attachment test of the reader.</summary>
public static class FilesTree
{
    /// <summary>Directories as a sorted depth-first list (siblings by lower-cased name); dot folders (.Trash) and anything deeper than 20 levels are left out.</summary>
    public static IReadOnlyList<FileFolderRow> FolderTree(IReadOnlyList<JsonNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var dirs = nodes.Where(IsDir).ToList();
        var output = new List<FileFolderRow>();
        Add(dirs, null, 0, output);
        return output;
    }

    /// <summary>A FileNode create object in the server's shape: draft shape (@type File, mediaType) or Stalwart 0.16 (type).</summary>
    public static JsonObject FileNode(bool draft, string? parentId, string name, string blobId, string mime)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(blobId);
        ArgumentNullException.ThrowIfNull(mime);
        return draft
            ? new JsonObject { ["@type"] = "File", ["parentId"] = parentId, ["name"] = name, ["blobId"] = blobId, ["mediaType"] = mime }
            : new JsonObject { ["parentId"] = parentId, ["name"] = name, ["blobId"] = blobId, ["type"] = mime };
    }

    /// <summary>True for a message/rfc822 part or an .eml file (so it can be opened as a message).</summary>
    public static bool IsMessageAttachment(JsonNode attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return IsMessageAttachment(attachment.Str("type"), attachment.Str("name"));
    }

    /// <summary>The same test on typed values.</summary>
    public static bool IsMessageAttachment(string? type, string? name)
    {
        var t = (type ?? string.Empty).ToLowerInvariant();
        return t is "message/rfc822" or "message/global" || (name is not null && name.ToLowerInvariant().EndsWith(".eml", StringComparison.Ordinal));
    }

    private static bool IsDir(JsonNode n)
    {
        var kind = n.Str("nodeType") ?? n.Str("@type") switch
        {
            null => null,
            "Directory" => "directory",
            "File" => "file",
            _ => "other",
        };
        return kind is null ? n["blobId"] is null : string.Equals(kind, "directory", StringComparison.Ordinal);
    }

    private static void Add(List<JsonNode> dirs, string? parent, int depth, List<FileFolderRow> output)
    {
        var kids = dirs
            .Where(n => string.Equals(n.Str("parentId") is { Length: > 0 } p ? p : null, parent, StringComparison.Ordinal))
            .OrderBy(n => (n.Str("name") ?? string.Empty).ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
        foreach (var k in kids)
        {
            var name = k.Str("name") ?? string.Empty;
            if (name.StartsWith('.') || depth > 20)
            {
                continue;
            }

            var id = k.Str("id") ?? string.Empty;
            output.Add(new FileFolderRow(id, name, depth));
            Add(dirs, id, depth + 1, output);
        }
    }
}
