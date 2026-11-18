// Mirrors: Bulwark components/files/file-views.tsx, file-list-layout.ts (sort, folders first); Rust files.rs render (name order); G-015, B-FILE-014. Contract from the M5 plan section 4.11; owner files-ui.

using Barbican.Core.Common;
using Barbican.Core.Files;

namespace Barbican.Presentation.Files;

/// <summary>The pure rules of the file list.</summary>
public static class FilesListRules
{
    /// <summary>Folders first (whatever the key and direction), then by the key: Name natural and case-insensitive ("file2" before "file10"), Modified, Size, Type (media type, then name); a tie falls back to Name; the order is stable.</summary>
    public static IReadOnlyList<FileEntry> Sort(IReadOnlyList<FileEntry> entries, FilesSortKey key, bool descending) => throw new PortPendingException("FilesListRules.Sort");

    /// <summary>Hidden files (a name starting with a dot) are dropped unless <paramref name="showHidden"/>; the trash folder is always dropped (it is a place of its own).</summary>
    public static IReadOnlyList<FileEntry> Visible(IReadOnlyList<FileEntry> entries, bool showHidden) => throw new PortPendingException("FilesListRules.Visible");
}
