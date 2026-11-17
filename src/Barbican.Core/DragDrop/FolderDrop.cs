// Mirrors: Rust dnd.rs:42 already_in, :149 plan_folder_drop; Bulwark lib/folder-tree-dnd.ts:41-134 (reorder and reparent rules);
// RFC 8621 §2.5 (Mailbox/set: parentId, sortOrder). Stalwart crates/jmap/src/mailbox/set.rs. Stub from stage F of M2; owner actions.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.DragDrop;

/// <summary>The pure decisions behind dropping mail or a folder onto a folder.</summary>
public static class FolderDrop
{
    /// <summary>Whether the message's only folder is already <paramref name="folderId"/> (a move there changes nothing).</summary>
    public static bool AlreadyIn(JsonNode message, string folderId)
    {
        throw new PortPendingException("dnd.rs:42 already_in");
    }

    /// <summary>
    /// Mailbox/set patches for dropping folder <paramref name="drag"/> onto <paramref name="target"/>: Inside nests it (last), Before and
    /// After put it next to the target under the target's parent. Only what changes is written; role folders keep their sortOrder.
    /// A cycle, an unknown folder or moving a role folder to another parent is refused with a user message.
    /// </summary>
    public static Result<IReadOnlyList<FolderPatch>> Plan(IReadOnlyList<JsonObject> folders, string drag, string target, DropZone zone)
    {
        throw new PortPendingException("dnd.rs:149 plan_folder_drop");
    }
}
