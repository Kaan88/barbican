// Mirrors: Rust listsync.rs:25 SyncStats; design/MOTION.md "Lists". Contract stub from F2; owner mailcore.

namespace Barbican.Presentation.Common;

/// <summary>What one sync did to the collection (the notifications the view got).</summary>
/// <param name="Set">Rows replaced in place.</param>
/// <param name="Inserted">Rows inserted.</param>
/// <param name="Removed">Rows removed.</param>
/// <param name="Moved">Rows moved (a remove plus an insert each, counted here and not in Inserted/Removed).</param>
/// <param name="Reset">The whole collection was replaced in one go.</param>
public readonly record struct SyncStats(int Set = 0, int Inserted = 0, int Removed = 0, int Moved = 0, bool Reset = false)
{
    /// <summary>Nothing was notified: the view does not repaint.</summary>
    public bool Untouched => this == default;
}
