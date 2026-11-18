// Mirrors: see IFilesViewModel. Stub from the M5 plan section 4.11; owner files-ui replaces the class (constructor dependencies: IFileStore, IFileWriter, ISettingsStore, IHookBus, TimeProvider).

using System.ComponentModel;
using Barbican.Core.Common;
using Barbican.Core.Files;

namespace Barbican.Presentation.Files;

/// <summary>The default <see cref="IFilesViewModel"/>.</summary>
public sealed class FilesViewModel : IFilesViewModel
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public string? Location => throw new PortPendingException("FilesViewModel.Location");

    public IReadOnlyList<FileEntry> Entries => throw new PortPendingException("FilesViewModel.Entries");

    public IReadOnlySet<string> SelectedKeys => throw new PortPendingException("FilesViewModel.SelectedKeys");

    public FilesSortKey SortKey => throw new PortPendingException("FilesViewModel.SortKey");

    public bool SortDescending => throw new PortPendingException("FilesViewModel.SortDescending");

    public FilesViewMode ViewMode => throw new PortPendingException("FilesViewModel.ViewMode");

    public string SearchText
    {
        get => throw new PortPendingException("FilesViewModel.SearchText");
        set => throw new PortPendingException("FilesViewModel.SearchText");
    }

    public bool IsLoading => throw new PortPendingException("FilesViewModel.IsLoading");

    public string? LoadError => throw new PortPendingException("FilesViewModel.LoadError");

    public void Navigate(string? location) => throw new PortPendingException("FilesViewModel.Navigate");

    public void SelectFile(string key, bool extend) => throw new PortPendingException("FilesViewModel.SelectFile");

    public Task RefreshAsync(CancellationToken ct) => throw new PortPendingException("FilesViewModel.RefreshAsync");
}
