// The keys being dragged inside the window (in-window drag and drop, B-FILE-013: the list rows start it, the tree rows, the trash row and folder rows receive it). The OS never sees these items.

namespace Barbican.App.Views.Files;

/// <summary>The drag in progress.</summary>
public static class FilesDrag
{
    /// <summary>The keys of the entries being dragged, or null.</summary>
    public static IReadOnlyList<string>? Keys { get; set; }
}
