// Mirrors: Windows.ApplicationModel.DataTransfer.DataPackageOperation (None 0, Copy 1, Move 2, Link 4), so the views cast it both ways. Owner actions.

namespace Barbican.Presentation.DragDrop;

/// <summary>The operations of a drag (the same values as the platform's DataPackageOperation).</summary>
[Flags]
public enum DragOperations
{
    /// <summary>No drop here.</summary>
    None = 0,

    /// <summary>A copy.</summary>
    Copy = 1,

    /// <summary>A move.</summary>
    Move = 2,

    /// <summary>A link (a tag added to a message).</summary>
    Link = 4,
}
