// Mirrors: Rust files.rs render (the sidebar), Bulwark components/files/files-sidebar.tsx (My files tree, Favorites, Recent, Shared with me, Trash); spec/menus.md MENU-FILE-FAV, MENU-FILE-RECENT; B-FILE-017 (the restyle). Items dropped on a folder row move there; dropped on Trash they are deleted (in-window drag and drop).
// A row only calls the model; the menus are the providers' (FilesMenus).
// Bulwark (WP-14, FL-04): files-sidebar.tsx:287-311 (header h-14 with the title and a plus icon button that opens the New menu, border-b, bg-secondary), :33-42 (rows min-h-8, square, active bg-accent text-accent-foreground font-medium, hover bg-muted: the shared ListItem.Row), :350 (sections mt-3), :250-259 (section label text-xs muted).

using Barbican.App.Shell;
using Microsoft.UI.Xaml.Automation;
using Barbican.App.Theme;
using Barbican.App.Views.Calendar;
using Barbican.Core.Files;
using Barbican.Presentation.Files;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass; splitting it would only pass the same locals around
namespace Barbican.App.Views.Files;

/// <summary>The Files sidebar.</summary>
public sealed class FilesSidebarView : UserControl
{
    private readonly FilesViewModel _vm = FilesUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly StackPanel _stack = new() { Spacing = 12 };
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private readonly List<Border> _tiles = [];
    private readonly Button _new;
    private readonly Border _head = new() { Height = 57, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 0, 12, 0) };
    private readonly ScrollViewer _scroll;
    private StackPanel _group = new() { Spacing = 2 };

    public FilesSidebarView()
    {
        Width = 236;
        Margin = new Thickness(7, -5, 5, 0); // lines the pill up with the calendar and contacts sidebars
        _new = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Primary"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { CalendarVisuals.Icon("Plus", 16, 2.2), new TextBlock { Text = "New", VerticalAlignment = VerticalAlignment.Center } } },
        };
        AutomationProperties.SetAutomationId(_new, "files.new");
        AutomationProperties.SetName(_new, "New");
        _new.Click += (_, _) => OpenNew(_new);
        _scroll = new ScrollViewer { Content = _stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 12) };
        var column = new Grid();
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.Children.Add(_head);
        Grid.SetRow(_scroll, 1);
        column.Children.Add(_scroll);
        Content = column;
        AutomationProperties.SetAutomationId(this, "files.sidebar");
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FilesViewModel.Location) or nameof(FilesViewModel.Entries))
            {
                Render();
            }
        };
        _look.PropertyChanged += (_, _) => Render();
        Render();
    }

    /// <summary>Redraws the places and the tree from the model.</summary>
    public void Render()
    {
        _tiles.Clear();
        _stack.Children.Clear();
        Head();
        _group = new StackPanel { Spacing = 2 };
        Place("My files", "Folder", null, "files.place.root", 0);
        Tree(null, 1);
        AddTile();
        if (!_look.IsBulwark)
        {
            Section("Starred");
        }

        Place("Starred", "Star", FilesViewModel.FavouritesLocation, "files.place.favourites", 0);
        foreach (var key in FilesUi.Actions.Favourites().Take(8))
        {
            if (FilesUi.Actions.Find(key) is { } e)
            {
                Row(e.Name, e.IsDirectory ? "Folder" : "File", e.Key, false, 1, "files.fav." + e.Key, FilesMenuIds.Favourite);
            }
        }

        AddTile();
        Place("Recent", "Clock", FilesViewModel.RecentLocation, "files.place.recent", 0);
        Place("Shared with me", "Users", FilesViewModel.SharedLocation, "files.place.shared", 0);
        var trash = Place("Trash", "Trash", FileTrash.TrashLocation, "files.place.trash", 0);
        trash.AllowDrop = true;
        trash.DragOver += (_, e) => e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        trash.Drop += (_, e) =>
        {
            if (FilesDrag.Keys is { Count: > 0 } keys)
            {
                FilesUi.Run(() => FilesUi.Actions.DeleteAsync(keys, CancellationToken.None));
                e.Handled = true;
            }
        };
        AddTile();
        ApplyLook();
    }

    // Bulwark: the title and a plus icon button in the h-14 header; the legacy styles keep the full-width New pill at the top of the list
    private void Head()
    {
        _head.Child = null;
        _head.Visibility = _look.IsBulwark ? Visibility.Visible : Visibility.Collapsed;
        _head.BorderBrush = FilesVisuals.Res("Line");
        if (_look.IsBulwark)
        {
            Button? plus = null;
            plus = FilesVisuals.IconButton(_look, "Plus", "files.new", "New", () => OpenNew(plus!));
            var title = new TextBlock { Text = "Files", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Children.Add(title);
            Grid.SetColumn(plus, 1);
            bar.Children.Add(plus);
            _head.Child = bar;
            return;
        }

        _stack.Children.Add(_new);
    }

    private static void OpenNew(FrameworkElement anchor)
    {
        var p = anchor.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(anchor.ActualWidth, anchor.ActualHeight));
        FilesUi.ShowMenu(FilesMenuIds.Background, p.X, p.Y, []);
    }

    private void ApplyLook()
    {
        var bento = _look.IsBento && !_look.IsBulwark;
        Margin = _look.IsBulwark ? new Thickness(0) : new Thickness(7, -5, 5, 0);
        _scroll.Padding = _look.IsBulwark ? new Thickness(0, 4, 0, 12) : new Thickness(0, 0, 0, 12);
        _new.Height = bento ? 52 : 46;
        _new.CornerRadius = new CornerRadius(bento ? 20 : 16);
        _new.FontSize = 14.5;
        foreach (var tile in _tiles)
        {
            tile.Background = bento ? _look.TileBrush : null;
            tile.BorderBrush = bento ? CalendarVisuals.Palette("Line") : null;
            tile.BorderThickness = new Thickness(bento ? 1 : 0);
            tile.CornerRadius = bento ? _look.PaneRadius : new CornerRadius(0);
            tile.Padding = bento ? new Thickness(12) : _look.IsBulwark ? new Thickness(0) : new Thickness(4, 0, 4, 0);
        }
    }

    private void AddTile()
    {
        var tile = new Border { Child = _group };
        _tiles.Add(tile);
        _stack.Children.Add(tile);
        _group = new StackPanel { Spacing = 2 };
    }

    private void Section(string text) => _group.Children.Add(new TextBlock { Text = text, FontSize = _look.IsBulwark ? 12 : 11.5, FontWeight = _look.IsBulwark ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.Bold, Foreground = CalendarVisuals.Palette("Dim"), Margin = _look.IsBulwark ? new Thickness(12, 12, 0, 4) : new Thickness(2, 0, 0, 4), IsHitTestVisible = false });

    private void Tree(string? parent, int depth)
    {
        foreach (var f in _vm.Folders(parent))
        {
            var hasKids = _vm.Folders(f.Key).Count > 0;
            var expanded = _open.Contains(f.Key);
            var row = Row(f.Name, "Folder", f.Key, hasKids, depth, "files.tree." + f.Key, FilesMenuIds.File, expanded);
            row.AllowDrop = true;
            row.DragOver += (_, e) => e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            row.Drop += (_, e) =>
            {
                if (FilesDrag.Keys is { Count: > 0 } keys)
                {
                    FilesUi.Run(() => FilesUi.Actions.MoveAsync(keys, f.Key, CancellationToken.None));
                    e.Handled = true;
                }
            };
            if (expanded)
            {
                Tree(f.Key, depth + 1);
            }
        }
    }

    private ListViewItem Place(string text, string icon, string? location, string id, int depth)
    {
        var active = string.Equals(_vm.Location, location, StringComparison.Ordinal);
        var b = Make(text, icon, active, depth, id);
        b.Tapped += (_, _) => _vm.Navigate(location);
        _group.Children.Add(b);
        return b;
    }

    private ListViewItem Row(string text, string icon, string key, bool hasKids, int depth, string id, string menu, bool expanded = false)
    {
        var active = string.Equals(_vm.Location, key, StringComparison.Ordinal);
        var b = Make(text, icon, active, depth, id, hasKids ? (expanded ? "ChevronDown" : "ChevronRight") : null);
        b.Tapped += (_, _) =>
        {
            if (string.Equals(menu, FilesMenuIds.Favourite, StringComparison.Ordinal))
            {
                FilesUi.Actions.Open(key);
                return;
            }

            _vm.Navigate(key);
            if (hasKids && !_open.Remove(key))
            {
                _ = _open.Add(key);
            }

            Render();
        };
        b.RightTapped += (_, e) =>
        {
            var p = e.GetPosition(null);
            FilesUi.ShowMenu(menu, p.X, p.Y, [key]);
            e.Handled = true;
        };
        _group.Children.Add(b);
        return b;
    }

    private ListViewItem Make(string text, string icon, bool active, int depth, string id, string? chevron = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness((_look.IsBulwark ? 12 : 8) + depth * 14, 0, 4, 0) };
        if (chevron is not null)
        {
            content.Children.Add(CalendarVisuals.Icon(chevron, 14, 2));
        }
        else if (_look.IsBulwark)
        {
            content.Children.Add(new Border { Width = 14 });
        }

        var fg = active && _look.IsBulwark ? FilesVisuals.Res("FeedbackSelectedFg") : CalendarVisuals.Palette(active ? "SelText" : "Body");
        var glyph = CalendarVisuals.Icon(icon, 16);
        if (_look.IsBulwark)
        {
            glyph.Foreground = active ? fg : FilesVisuals.Res("Dim");
        }

        content.Children.Add(glyph);
        content.Children.Add(new TextBlock { Text = text, FontSize = _look.IsBulwark ? 14 : 13, FontWeight = active ? (_look.IsBulwark ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.SemiBold) : Microsoft.UI.Text.FontWeights.Normal, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 });
        var row = new ListViewItem { Style = FilesVisuals.Sheet("ListItem.Row"), Content = content, Height = _look.FilesSidebarRowHeight, CornerRadius = _look.FilesSidebarRowRadius, IsSelected = active, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(row, id);
        AutomationProperties.SetName(row, text);
        return row;
    }
}
