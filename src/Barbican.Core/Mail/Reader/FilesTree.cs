// Mirrors: Rust reader.rs:887 folder_tree, :949 file_node, :728 is_message_attachment; JMAP FileNode (draft-ietf-jmap-filenode); Stalwart crates/jmap/src/file/{get,query,set}.rs;
// Bulwark components/email/save-to-files-dialog.tsx, lib/files-save.ts, reader-attachments.tsx. Stub from stage F of M2; owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Reader;

/// <summary>FileNode helpers for Save to Files and the message-attachment test of the reader.</summary>
public static class FilesTree
{
    /// <summary>Directories as a sorted depth-first list (siblings by lower-cased name); dot folders (.Trash) and anything deeper than 20 levels are left out.</summary>
    public static IReadOnlyList<FileFolderRow> FolderTree(IReadOnlyList<JsonNode> nodes)
    {
        throw new PortPendingException("reader.rs:887 folder_tree");
    }

    /// <summary>A FileNode create object in the server's shape: draft shape (@type File, mediaType) or Stalwart 0.16 (type).</summary>
    public static JsonObject FileNode(bool draft, string? parentId, string name, string blobId, string mime)
    {
        throw new PortPendingException("reader.rs:949 file_node");
    }

    /// <summary>True for a message/rfc822 part or an .eml file (so it can be opened as a message).</summary>
    public static bool IsMessageAttachment(JsonNode attachment)
    {
        throw new PortPendingException("reader.rs:728 is_message_attachment");
    }
}
