// Mirrors: Rust files.rs (ST, render, open, activate), Bulwark components/files/files-app.tsx, file-browser.tsx, files-sidebar.tsx; I-066 (account scope), the Files rows of interactions.csv. Hooks: onFileNavigate, onFileSelectionChange, onFileUndo.
// Contract from the M5 plan section 4.11; owner files-ui implements.

using System.ComponentModel;
using Barbican.Core.Files;

namespace Barbican.Presentation.Files;

/// <summary>The state of the Files surface: the place (a folder, Favourites, Recent, Shared with me, the trash), the list, the selection, sort and view mode. One per window; <c>PropertyChanged</c> is raised on the UI thread.</summary>
public interface IFilesViewModel : INotifyPropertyChanged
{
    /// <summary>A folder key, or one of "*favourites", "*recent", "*shared", <see cref="FileTrash.TrashLocation"/>; null is the root of My files.</summary>
    string? Location { get; }

    IReadOnlyList<FileEntry> Entries { get; }

    IReadOnlySet<string> SelectedKeys { get; }

    FilesSortKey SortKey { get; }

    bool SortDescending { get; }

    FilesViewMode ViewMode { get; }

    string SearchText { get; set; }

    bool IsLoading { get; }

    string? LoadError { get; }

    /// <summary>Opens a place; fires onFileNavigate.</summary>
    void Navigate(string? location);

    void SelectFile(string key, bool extend);

    Task RefreshAsync(CancellationToken ct);
}
