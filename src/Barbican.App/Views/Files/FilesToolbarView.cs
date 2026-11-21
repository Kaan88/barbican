// Mirrors: Rust files.rs render (the top bar), Bulwark components/files/files-header.tsx: :111 (header h-14, px-3, border-b), :152-168 (breadcrumb: px-1.5 py-1 rounded-md, last bold, others muted until hovered), :313-338 (Empty trash, Paste: outline xs buttons),
// :340 (search w-56), :341-360 (sort icon button opening the sort menu), :361-385 (list and grid radio group, h-8 rounded-md border p-0.5, w-7 buttons, active = TOOLBAR_ACTIVE), :386-390 (Refresh), :414 (Details toggle),
// :192-258 (with a selection the header shows the clear button, "N selected" and icon buttons: Open, Download, Rename, Move to, Cut, Copy, Delete in destructive tint; in the trash Restore and Delete permanently). ui/toolbar.tsx:16-29 (active tint).
// spec/menus.md "The toolbar's Empty trash button opens the confirmation directly". The sort button opens MENU-FILES-SORT in-window (ADR 0007). WP-14, FL-01.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Calendar;
using Barbican.Core.Files;
using Barbican.Presentation.Files;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass; splitting it would only pass the same locals around
namespace Barbican.App.Views.Files;

/// <summary>The bar over the list.</summary>
public sealed class FilesToolbarView : UserControl
{
    private readonly FilesViewModel _vm = FilesUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly Border _bar = new() { Height = 57, Padding = new Thickness(12, 0, 12, 0), BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _tools = new() { ColumnSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { PlaceholderText = "Search this folder", Width = 224, Height = 32, MinHeight = 32, FontSize = 13 };
    private readonly Border _viewGroup = new() { Height = 32, BorderThickness = new Thickness(1), Padding = new Thickness(2) };
    private readonly ToggleButton _list;
    private readonly ToggleButton _grid;
    private readonly ToggleButton _details;
    private readonly Button _sort;
    private readonly Button _refresh;
    private readonly Button _empty;
    private readonly Button _paste;
    private readonly StackPanel _selection = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _count = new() { FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private bool _syncing;

    public FilesToolbarView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _sort = FilesVisuals.IconButton(_look, "Filter", "files.sort", "Sort by", () => OpenSort());
        _refresh = FilesVisuals.IconButton(_look, "Refresh", "files.refresh", "Refresh", () => FilesUi.RunCommand(FilesCommandIds.Refresh));
        _empty = FilesVisuals.Outline(_look, "Empty trash", "Trash", "files.empty-trash", () => FilesUi.RunCommand(FilesCommandIds.EmptyTrash), destructive: true);
        _paste = FilesVisuals.Outline(_look, "Paste", "File", "files.paste", () => FilesUi.RunCommand(FilesCommandIds.Paste));
        _list = FilesVisuals.Toggle(_look, "List", "files.view.list", "List view", 28);
        _grid = FilesVisuals.Toggle(_look, "Package", "files.view.grid", "Grid view", 28);
        _details = FilesVisuals.Toggle(_look, "Info", "files.details-toggle", "Details");
        _list.Click += (_, _) => SetMode(FilesViewMode.List);
        _grid.Click += (_, _) => SetMode(FilesViewMode.Grid);
        _details.Click += (_, _) => FilesUi.RunCommand(FilesCommandIds.Details);
        AutomationProperties.SetAutomationId(_viewGroup, "files.view-toggle");
        _viewGroup.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _list, _grid } };
        var rail = new Grid { ColumnSpacing = 4 };
        rail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rail.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rail.Children.Add(_crumbs);
        Grid.SetColumn(_tools, 1);
        rail.Children.Add(_tools);
        rail.Children.Add(_selection);
        _bar.Child = rail;
        Content = _bar;
        foreach (var (el, i) in new FrameworkElement[] { _empty, _paste, _search, _sort, _viewGroup, _refresh, _details }.Select((e, i) => (e, i)))
        {
            el.VerticalAlignment = VerticalAlignment.Center;
            _tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(el, i);
            _tools.Children.Add(el);
        }

        AutomationProperties.SetAutomationId(_search, "files.search");
        AutomationProperties.SetName(_search, "Search this folder");
        _search.TextChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _vm.SearchText = _search.Text;
            }
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                _vm.SearchText = string.Empty;
                e.Handled = true;
            }
        };
        _vm.PropertyChanged += (_, _) => Render();
        FilesUi.Actions.ClipboardChanged += Render;
        _look.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void SetMode(FilesViewMode mode)
    {
        if (_vm.ViewMode != mode)
        {
            FilesUi.RunCommand(FilesCommandIds.ToggleView);
        }

        Render();
    }

    private void OpenSort()
    {
        var p = _sort.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(_sort.ActualWidth, _sort.ActualHeight));
        FilesUi.ShowMenu(FilesMenuIds.Sort, p.X, p.Y, []);
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
        _bar.BorderBrush = FilesVisuals.Res("Line");
        var picked = _vm.SelectedKeys.Count > 0;
        _bar.Background = picked && _look.IsBulwark ? FilesVisuals.Res("FeedbackHover") : FilesVisuals.Clear;
        _crumbs.Visibility = picked ? Visibility.Collapsed : Visibility.Visible;
        _tools.Visibility = picked ? Visibility.Collapsed : Visibility.Visible;
        _selection.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        _list.IsChecked = _vm.ViewMode == FilesViewMode.List;
        _grid.IsChecked = _vm.ViewMode == FilesViewMode.Grid;
        _details.IsChecked = _vm.ShowDetails;
        _viewGroup.CornerRadius = _look.ButtonRadius;
        _viewGroup.BorderBrush = FilesVisuals.Res("Line");
        _empty.Visibility = _vm.InTrash ? Visibility.Visible : Visibility.Collapsed;
        _empty.IsEnabled = _vm.Entries.Count > 0;
        _paste.Visibility = FilesUi.Actions.CanPaste && !_vm.InTrash ? Visibility.Visible : Visibility.Collapsed;
        _refresh.IsEnabled = !_vm.IsLoading;
        Crumbs(picked);
        if (picked)
        {
            Selection();
        }
    }

    private void Crumbs(bool last)
    {
        _crumbs.Children.Clear();
        var trail = new List<(string Name, Action Go, string Id)> { (Title(), () => _vm.Navigate(_vm.Location is { } l && l.StartsWith('*') ? l : null), "files.crumb.root") };
        trail.AddRange(_vm.Breadcrumb.Select(f => (f.Name, (Action)(() => _vm.Navigate(f.Key)), "files.crumb." + f.Key)));
        for (var i = 0; i < trail.Count; i++)
        {
            if (last && i < trail.Count - 1)
            {
                continue;
            }

            if (i > 0 && !last)
            {
                _crumbs.Children.Add(CalendarVisuals.Icon("ChevronRight", 14, 2));
                ((FrameworkElement)_crumbs.Children[^1]).Opacity = 0.6;
                ((FrameworkElement)_crumbs.Children[^1]).VerticalAlignment = VerticalAlignment.Center;
            }

            Crumb(trail[i].Name, trail[i].Go, trail[i].Id, i == trail.Count - 1);
        }
    }

    private string Title() => _vm.Location switch
    {
        FileTrash.TrashLocation => "Trash",
        FilesViewModel.FavouritesLocation => "Starred",
        FilesViewModel.RecentLocation => "Recent",
        FilesViewModel.SharedLocation => "Shared with me",
        _ => "My files",
    };

    // files-header.tsx:152-168: px-1.5 py-1 rounded-md, the current folder semibold in the text colour, the way up muted until hovered
    private void Crumb(string text, Action go, string id, bool current)
    {
        var b = new Button
        {
            Style = FilesVisuals.Sheet("Button.Icon"),
            Content = new TextBlock { Text = text, FontSize = 14, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 256 },
            Foreground = FilesVisuals.Res(current ? "Text" : "Dim"),
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            CornerRadius = _look.ButtonRadius,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(b, id);
        AutomationProperties.SetName(b, text);
        b.Click += (_, _) => go();
        _crumbs.Children.Add(b);
    }

    // files-header.tsx:192-258
    private void Selection()
    {
        _selection.Children.Clear();
        _selection.Children.Add(FilesVisuals.IconButton(_look, "Close", "files.selection.clear", "Clear selection", _vm.ClearSelection));
        _selection.Children.Add(new Border { Width = 1, Height = 20, Background = FilesVisuals.Res("Line"), Margin = new Thickness(4, 0, 8, 0) });
        _count.Text = _vm.SelectedKeys.Count == 1 ? "1 selected" : _vm.SelectedKeys.Count + " selected";
        AutomationProperties.SetAutomationId(_count, "files.selection.count");
        _selection.Children.Add(_count);
        void Tool(string icon, string id, string name, string command, bool destructive = false) =>
            _selection.Children.Add(FilesVisuals.IconButton(_look, icon, "files.selection." + id, name, () => FilesUi.RunCommand(command), destructive));
        var sel = FilesUi.Actions.Selected;
        if (_vm.InTrash)
        {
            Tool("Undo", "restore", "Restore", FilesCommandIds.Restore);
            Tool("Trash", "purge", "Delete permanently", FilesCommandIds.Purge, true);
            return;
        }

        if (sel.Count == 1)
        {
            Tool("Folder", "open", "Open", FilesCommandIds.Open);
        }

        if (sel.Any(e => !e.IsDirectory) || sel.Count > 1)
        {
            Tool("Download", "download", "Download", FilesCommandIds.Download);
        }

        if (sel.Count == 1)
        {
            Tool("Pen", "rename", "Rename", FilesCommandIds.Rename);
        }

        Tool("Move", "move", "Move to…", FilesCommandIds.MoveDialog);
        Tool("Tpl", "cut", "Cut", FilesCommandIds.Cut);
        Tool("File", "copy", "Copy", FilesCommandIds.Copy);
        Tool("Trash", "delete", "Delete", FilesCommandIds.Delete, true);
    }
}
