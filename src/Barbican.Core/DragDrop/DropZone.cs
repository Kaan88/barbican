// Mirrors: Rust dnd.rs:149 (the zone strings "inside", "before", "after"); Bulwark lib/folder-tree-dnd.ts:41-134.

namespace Barbican.Core.DragDrop;

/// <summary>Where on the target folder a dragged folder was dropped.</summary>
public enum DropZone
{
    /// <summary>Nested under the target (last child).</summary>
    Inside,

    /// <summary>Next to the target, before it, under the target's parent.</summary>
    Before,

    /// <summary>Next to the target, after it, under the target's parent.</summary>
    After,
}
