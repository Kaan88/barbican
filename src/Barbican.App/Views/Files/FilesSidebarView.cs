// Mirrors: Rust files.rs render (the sidebar), Bulwark components/files/files-sidebar.tsx (My files tree, Favorites, Recent, Shared with me, Trash); spec/menus.md MENU-FILE-FAV, MENU-FILE-RECENT; B-FILE-017 (the restyle). Items dropped on a folder row move there; dropped on Trash they are deleted (in-window drag and drop).
// A row only calls the model; the menus are the providers' (FilesMenus).

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
    private StackPanel _group = new() { Spacing = 2 };

    public FilesSidebarView()
    {
        Width = 248;
        _new = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Primary"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { CalendarVisuals.Icon("Plus", 16, 2.2), new TextBlock { Text = "New", VerticalAlignment = VerticalAlignment.Center } } },
        };
        AutomationProperties.SetAutomationId(_new, "files.new");
        AutomationProperties.SetName(_new, "New");
        _new.Click += (_, _) =>
        {
            var p = _new.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, _new.ActualHeight));
            FilesUi.ShowMenu(FilesMenuIds.Background, p.X, p.Y, []);
        };
        Content = new ScrollViewer { Content = _stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 12) };
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
        _stack.Children.Add(_new);
        _group = new StackPanel { Spacing = 2 };
        Place("My files", "Folder", null, "files.place.root", 0);
        Tree(null, 1);
        AddTile();
        Section("Favourites");
        Place("Favourites", "Star", FilesViewModel.FavouritesLocation, "files.place.favourites", 0);
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

    private void ApplyLook()
    {
        var bento = _look.IsBento;
        _new.Height = bento ? 52 : 46;
        _new.CornerRadius = new CornerRadius(bento ? 20 : 16);
        _new.FontSize = 14.5;
        foreach (var tile in _tiles)
        {
            tile.Background = bento ? _look.TileBrush : null;
            tile.BorderBrush = bento ? CalendarVisuals.Palette("Line") : null;
            tile.BorderThickness = new Thickness(bento ? 1 : 0);
            tile.CornerRadius = bento ? _look.PaneRadius : new CornerRadius(0);
            tile.Padding = bento ? new Thickness(12) : new Thickness(4, 0, 4, 0);
        }
    }

    private void AddTile()
    {
        var tile = new Border { Child = _group };
        _tiles.Add(tile);
        _stack.Children.Add(tile);
        _group = new StackPanel { Spacing = 2 };
    }

    private void Section(string text) => _group.Children.Add(new TextBlock { Text = text, FontSize = 11.5, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = CalendarVisuals.Palette("Dim"), Margin = new Thickness(2, 0, 0, 4), IsHitTestVisible = false });

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

    private Grid Place(string text, string icon, string? location, string id, int depth)
    {
        var active = string.Equals(_vm.Location, location, StringComparison.Ordinal);
        var b = Make(text, icon, active, depth, id);
        b.Tapped += (_, _) => _vm.Navigate(location);
        _group.Children.Add(b);
        return b;
    }

    private Grid Row(string text, string icon, string key, bool hasKids, int depth, string id, string menu, bool expanded = false)
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

    private static Grid Make(string text, string icon, bool active, int depth, string id, string? chevron = null)
    {
        var row = new Grid { Height = 32, CornerRadius = new CornerRadius(10), Padding = new Thickness(8 + depth * 14, 0, 4, 0), Background = active ? CalendarVisuals.Palette("Sel") : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (chevron is not null)
        {
            content.Children.Add(CalendarVisuals.Icon(chevron, 12, 2));
        }

        content.Children.Add(CalendarVisuals.Icon(icon, 16));
        content.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, Foreground = CalendarVisuals.Palette(active ? "SelText" : "Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 });
        row.Children.Add(content);
        AutomationProperties.SetAutomationId(row, id);
        AutomationProperties.SetName(row, text);
        return row;
    }
}
