// Mirrors: Rust files.rs render (the top bar), Bulwark components/files/files-toolbar.tsx, breadcrumb.tsx (breadcrumb, search, sort, list and grid toggle, details toggle, Empty trash in the trash); spec/menus.md "The toolbar's Empty trash button opens the confirmation directly".

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Views.Calendar;
using Barbican.Core.Files;
using Barbican.Presentation.Files;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass; splitting it would only pass the same locals around
namespace Barbican.App.Views.Files;

/// <summary>The bar over the list.</summary>
public sealed class FilesToolbarView : UserControl
{
    private readonly FilesViewModel _vm = FilesUi.Model;
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly TextBox _search = new() { PlaceholderText = "Search files", Width = 220 };
    private readonly Button _sort = new() { Style = (Style)Application.Current.Resources["Button.Action"] };
    private readonly Button _view = new() { Style = (Style)Application.Current.Resources["Button.Icon"], Content = CalendarVisuals.Icon("List", 16) };
    private readonly Button _details = new() { Style = (Style)Application.Current.Resources["Button.Icon"], Content = CalendarVisuals.Icon("Info", 16) };
    private readonly Button _empty = new() { Content = "Empty trash", Visibility = Visibility.Collapsed };
    private bool _syncing;

    public FilesToolbarView()
    {
        var g = new Grid { ColumnSpacing = 8, Margin = new Thickness(12, 8, 12, 8), Height = 40 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        g.Children.Add(_crumbs);
        Put(g, _empty, 1);
        Put(g, _search, 2);
        Put(g, _sort, 3);
        Put(g, _view, 4);
        Put(g, _details, 5);
        Content = g;
        AutomationProperties.SetAutomationId(_search, "files.search");
        AutomationProperties.SetAutomationId(_sort, "files.sort");
        AutomationProperties.SetAutomationId(_view, "files.view-toggle");
        AutomationProperties.SetAutomationId(_details, "files.details-toggle");
        AutomationProperties.SetAutomationId(_empty, "files.empty-trash");
        AutomationProperties.SetName(_view, "Toggle list and grid");
        AutomationProperties.SetName(_details, "Details");
        _search.TextChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _vm.SearchText = _search.Text;
            }
        };
        _sort.Click += (_, _) =>
        {
            _vm.SortKey = _vm.SortKey switch { FilesSortKey.Name => FilesSortKey.Modified, FilesSortKey.Modified => FilesSortKey.Size, FilesSortKey.Size => FilesSortKey.Type, _ => FilesSortKey.Name };
        };
        _view.Click += (_, _) => FilesUi.RunCommand(FilesCommandIds.ToggleView);
        _details.Click += (_, _) => FilesUi.RunCommand(FilesCommandIds.Details);
        _empty.Click += (_, _) => FilesUi.RunCommand(FilesCommandIds.EmptyTrash);
        _vm.PropertyChanged += (_, _) => Render();
        Render();
    }

    private static void Put(Grid g, FrameworkElement e, int col)
    {
        e.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    /// <summary>Redraws from the model.</summary>
    public void Render()
    {
        _syncing = true;
        if (!string.Equals(_search.Text, _vm.SearchText, StringComparison.Ordinal))
        {
            _search.Text = _vm.SearchText;
        }

        _syncing = false;
        _sort.Content = "Sort: " + _vm.SortKey + (_vm.SortDescending ? " ▼" : " ▲");
        _view.Content = CalendarVisuals.Icon(_vm.ViewMode == FilesViewMode.List ? "Package" : "List", 16);
        _empty.Visibility = _vm.InTrash ? Visibility.Visible : Visibility.Collapsed;
        _crumbs.Children.Clear();
        Crumb(Title(), () => _vm.Navigate(_vm.Location is { } l && l.StartsWith('*') ? l : null), "files.crumb.root");
        foreach (var f in _vm.Breadcrumb)
        {
            _crumbs.Children.Add(new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.5 });
            Crumb(f.Name, () => _vm.Navigate(f.Key), "files.crumb." + f.Key);
        }
    }

    private string Title() => _vm.Location switch
    {
        FileTrash.TrashLocation => "Trash",
        FilesViewModel.FavouritesLocation => "Favourites",
        FilesViewModel.RecentLocation => "Recent",
        FilesViewModel.SharedLocation => "Shared with me",
        _ => "My files",
    };

    private void Crumb(string text, Action go, string id)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Content = text };
        AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => go();
        _crumbs.Children.Add(b);
    }
}
