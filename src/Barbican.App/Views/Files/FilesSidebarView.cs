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
    private readonly StackPanel _stack = new() { Spacing = 2 };
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);

    public FilesSidebarView()
    {
        Width = 248;
        Content = new ScrollViewer { Content = _stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
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
        _stack.Children.Clear();
        var newButton = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Primary"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { CalendarVisuals.Icon("Plus", 16, 2.2), new TextBlock { Text = "New", VerticalAlignment = VerticalAlignment.Center } } },
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetAutomationId(newButton, "files.new");
        AutomationProperties.SetName(newButton, "New");
        newButton.Click += (_, _) =>
        {
            var p = newButton.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, newButton.ActualHeight));
            FilesUi.ShowMenu(FilesMenuIds.Background, p.X, p.Y, []);
        };
        _stack.Children.Add(newButton);

        Place("My files", "Folder", null, "files.place.root", 0);
        Tree(null, 1);
        Section("Favourites");
        Place("Favourites", "Star", FilesViewModel.FavouritesLocation, "files.place.favourites", 0);
        foreach (var key in FilesUi.Actions.Favourites().Take(8))
        {
            if (FilesUi.Actions.Find(key) is { } e)
            {
                Row(e.Name, e.IsDirectory ? "Folder" : "File", e.Key, false, 1, "files.fav." + e.Key, FilesMenuIds.Favourite);
            }
        }

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
    }

    private void Section(string text) => _stack.Children.Add(new TextBlock { Text = text.ToUpperInvariant(), FontSize = 10.5, Opacity = 0.55, Margin = new Thickness(10, 12, 0, 4), IsHitTestVisible = false });

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

    private Button Place(string text, string icon, string? location, string id, int depth)
    {
        var active = string.Equals(_vm.Location, location, StringComparison.Ordinal);
        var b = Make(text, icon, active, depth, id);
        b.Click += (_, _) => _vm.Navigate(location);
        _stack.Children.Add(b);
        return b;
    }

    private Button Row(string text, string icon, string key, bool hasKids, int depth, string id, string menu, bool expanded = false)
    {
        var active = string.Equals(_vm.Location, key, StringComparison.Ordinal);
        var b = Make(text, icon, active, depth, id, hasKids ? (expanded ? "ChevronDown" : "ChevronRight") : null);
        b.Click += (_, _) =>
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
        _stack.Children.Add(b);
        return b;
    }

    private static Button Make(string text, string icon, bool active, int depth, string id, string? chevron = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(depth * 14, 0, 0, 0) };
        if (chevron is not null)
        {
            row.Children.Add(CalendarVisuals.Icon(chevron, 12, 2));
        }

        row.Children.Add(CalendarVisuals.Icon(icon, 16));
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 });
        var b = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Action"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = 34,
            Content = row,
            Background = active ? CalendarVisuals.Palette("Sel") : null,
        };
        AutomationProperties.SetAutomationId(b, id);
        AutomationProperties.SetName(b, text);
        return b;
    }
}
