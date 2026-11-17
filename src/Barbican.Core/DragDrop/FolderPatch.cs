// Mirrors: Rust dnd.rs:149 plan_folder_drop (the (id, patch) pairs); RFC 8621 §2.5 (Mailbox/set update). Owner actions.

using System.Text.Json.Nodes;

namespace Barbican.Core.DragDrop;

/// <summary>One Mailbox/set update the drop needs: the folder id and the changed properties (sortOrder, parentId).</summary>
public sealed record FolderPatch(string Id, JsonObject Patch);
