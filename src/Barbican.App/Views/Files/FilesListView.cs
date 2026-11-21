// Mirrors: Rust files.rs render (the list), Bulwark components/files/file-views.tsx (:170-187 column header h-9 with sortable Name, Modified, Size; :242-248 row h-10, border-b border-border/60, hover and selected from the shared ListItem.Row; :295-310 the hover "more" button;
// :340-367 grid: Folders and Files headings, folder items h-11 rounded-lg border, file tiles with a muted 4:3 thumbnail area; :414-418, :461-464), file-empty-state.tsx and ui/empty-state.tsx (empty folder, no search results), file-list-layout.ts (list rows and grid tiles), file-browser.tsx (click, Ctrl and Shift click, double click, right click, F2 inline rename, the keys of B-FILE-012);
// spec/menus.md MENU-FILE, MENU-FILE-TRASH, MENU-FILES-BG. B-FILE-017 (restyle). The list and the grid are drawn in one pass from the model (no blank frame); settings: icons, coloured icons, thumbnails (D-M5-11).
// Rows start an in-window drag; folder rows receive it (move). An OS file drop on the list uploads (the Explorer drag itself is unproven, no real drag in a hidden run).

using Barbican.App.Shell;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Barbican.App.Theme;
using Barbican.App.Views.Calendar;
using Barbican.Core.Files;
using Barbican.Presentation.Files;
using Windows.System;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass; splitting it would only pass the same locals around
namespace Barbican.App.Views.Files;

/// <summary>The file list or grid of the current place.</summary>
public sealed class FilesListView : UserControl
{
    private readonly FilesViewModel _vm = FilesUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly Grid _root = new();
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly StackPanel _gridHost = new() { Spacing = 8, Padding = new Thickness(8) };
    private readonly Border _empty = new() { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Border _notice = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Top };
    private readonly Dictionary<string, ListViewItem> _rows = new(StringComparer.Ordinal);
    private string? _renaming;

    public FilesListView()
    {
        IsTabStop = true;
        AllowDrop = true;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Grid { Children = { _list, _gridHost } }, Padding = new Thickness(0) };
        _root.Children.Add(scroll);
        _root.Children.Add(_empty);
        _root.Children.Add(_notice);
        Content = _root;
        AutomationProperties.SetAutomationId(this, "files.list");
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FilesViewModel.Entries) or nameof(FilesViewModel.ViewMode) or nameof(FilesViewModel.LoadError) or nameof(FilesViewModel.Settings))
            {
                Render();
            }
            else if (e.PropertyName is nameof(FilesViewModel.SelectedKeys))
            {
                Paint();
            }
        };
        _look.PropertyChanged += (_, _) => Render();
        FilesUi.Actions.RenameRequested += key =>
        {
            _renaming = key;
            Render();
        };
        KeyDown += OnKey;
        RightTapped += (_, e) =>
        {
            if (!e.Handled)
            {
                var p = e.GetPosition(null);
                _vm.ClearSelection();
                FilesUi.ShowMenu(FilesMenuIds.Background, p.X, p.Y, []);
            }
        };
        DragOver += (_, e) => e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        Drop += (_, e) =>
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var view = e.DataView;
                FilesUi.Run(async () =>
                {
                    var items = await view.GetStorageItemsAsync();
                    var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                    _ = await FilesUi.Actions.UploadPathsAsync(paths, CancellationToken.None).ConfigureAwait(true);
                });
            }
        };
        Render();
    }

    /// <summary>Redraws every row or tile from the model.</summary>
    public void Render()
    {
        _rows.Clear();
        _list.Children.Clear();
        _gridHost.Children.Clear();
        var grid = _vm.ViewMode == FilesViewMode.Grid;
        _list.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        _gridHost.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        _notice.Visibility = _vm.LoadError is null ? Visibility.Collapsed : Visibility.Visible;
        if (_vm.LoadError is { } err)
        {
            var retry = new Button { Content = "Retry" };
            retry.Click += (_, _) => FilesUi.RunCommand(FilesCommandIds.Refresh);
            _notice.Background = CalendarVisuals.Palette("RoseBg");
            _notice.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { new TextBlock { Text = "Could not load files: " + err, Foreground = CalendarVisuals.Palette("RoseFg"), VerticalAlignment = VerticalAlignment.Center }, retry } };
        }

        _empty.Child = _vm.Entries.Count == 0 ? EmptyState() : null;
        _empty.Visibility = _vm.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (grid)
        {
            Tiles();
        }
        else if (_vm.Entries.Count > 0)
        {
            _list.Children.Add(Header());
            foreach (var e in _vm.Entries)
            {
                var row = Row(e);
                _rows[e.Key] = row;
                _list.Children.Add(row);
            }
        }

        Paint();
    }

    // Grid: a Folders heading with compact folder items, a Files heading with tiles (file-views.tsx:340-367)
    private void Tiles()
    {
        var folders = _vm.Entries.Where(e => e.IsDirectory).ToList();
        var files = _vm.Entries.Where(e => !e.IsDirectory).ToList();
        void Section(string title, IReadOnlyList<FileEntry> items, bool folder)
        {
            if (items.Count == 0)
            {
                return;
            }

            if (folders.Count > 0 && files.Count > 0)
            {
                _gridHost.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = FilesVisuals.Res("Dim"), Margin = new Thickness(2, 6, 0, 0) });
            }

            var host = new ItemsControl { ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' /></ItemsPanelTemplate>") };
            foreach (var e in items)
            {
                var tile = folder ? FolderItem(e) : Tile(e);
                _rows[e.Key] = tile;
                host.Items.Add(tile);
            }

            _gridHost.Children.Add(host);
        }

        Section("Folders", folders, true);
        Section("Files", files, false);
    }

    // ui/empty-state.tsx, files/file-empty-state.tsx: icon in a muted circle, title, one line, actions
    private FrameworkElement EmptyState()
    {
        if (_vm.IsLoading)
        {
            return new TextBlock { Text = "Loading…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = FilesVisuals.Res("Dim"), FontSize = 14, IsHitTestVisible = false };
        }

        if (_vm.SearchText.Length > 0)
        {
            return FilesVisuals.EmptyState("Search", "Nothing in this folder matches \"" + _vm.SearchText + "\"", null, FilesVisuals.Outline(_look, "Clear search", null, "files.empty.clear-search", () => _vm.SearchText = string.Empty));
        }

        return _vm.Location switch
        {
            FileTrash.TrashLocation => FilesVisuals.EmptyState("Trash", "Trash is empty", "Deleted files stay here until you empty the trash."),
            FilesViewModel.FavouritesLocation => FilesVisuals.EmptyState("Star", "Nothing starred yet", "Star a folder to find it here."),
            FilesViewModel.RecentLocation => FilesVisuals.EmptyState("Clock", "No recent files", null),
            FilesViewModel.SharedLocation => FilesVisuals.EmptyState("Users", "Nothing has been shared with you", null),
            _ => FilesVisuals.EmptyState(
                _vm.Location is null ? "Download" : "Folder",
                _vm.Location is null ? "No files yet" : "This folder is empty",
                "Drop files here, or upload them from your device.",
                PrimaryUpload(),
                FilesVisuals.Outline(_look, "New folder", "Plus", "files.empty.new-folder", () => FilesUi.RunCommand(FilesCommandIds.NewFolder))),
        };
    }

    private Button PrimaryUpload()
    {
        var b = new Button { Style = FilesVisuals.Sheet("Button.Primary"), Content = "Upload files", Height = 32, FontSize = 13, Padding = new Thickness(12, 0, 12, 0), CornerRadius = _look.ButtonRadius, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        FilesVisuals.Name(b, "files.empty.upload", "Upload files");
        b.Click += (_, _) => FilesUi.RunCommand(FilesCommandIds.Upload);
        return b;
    }

    // Selection and hover come from ListItem.Row (the shared feedback brushes); a grid tile also draws its border in the selected colour.
    private void Paint()
    {
        foreach (var (key, item) in _rows)
        {
            var picked = _vm.SelectedKeys.Contains(key);
            item.IsSelected = picked;
            if (item.Tag is Border outline)
            {
                outline.BorderBrush = picked && _look.IsBulwark ? Translucent("Accent", 0x99) : FilesVisuals.Res("Line");
            }

            if (item.Tag is not Border && item.Content is Border b && _look.IsBulwark)
            {
                b.BorderBrush = FilesVisuals.Res("Line");
            }
        }
    }

    private static SolidColorBrush Translucent(string key, byte alpha)
    {
        var c = FilesVisuals.Res(key) is SolidColorBrush s ? s.Color : Windows.UI.Color.FromArgb(255, 128, 128, 128);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    // file-views.tsx:170-187: the column header, 36 px, bottom border, text-xs medium muted; Name, Modified (from md) and Size are sortable
    private Grid Header()
    {
        var g = Columns();
        g.Height = 36;
        void Cell(string t, int col, FilesSortKey key, HorizontalAlignment align)
        {
            var active = key == _vm.SortKey;
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new TextBlock { Text = t, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center } } };
            if (active)
            {
                var arrow = CalendarVisuals.Icon("ChevronDown", 12, 2);
                arrow.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                arrow.RenderTransform = new RotateTransform { Angle = _vm.SortDescending ? 0 : 180 };
                label.Children.Add(arrow);
            }

            var b = new Button { Style = FilesVisuals.Sheet("Button.Icon"), Content = label, HorizontalAlignment = align, Height = 28, Padding = new Thickness(4, 0, 4, 0), Foreground = FilesVisuals.Res(active ? "Text" : "Dim"), CornerRadius = _look.ButtonRadius, VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => FilesSortRules.Apply(_vm, active ? (_vm.SortDescending ? "asc" : "desc") : key.ToString().ToLowerInvariant());
            FilesVisuals.Name(b, "files.sort." + t.ToLowerInvariant(), "Sort by " + t.ToLowerInvariant());
            Grid.SetColumn(b, col);
            g.Children.Add(b);
        }

        Cell("Name", 1, FilesSortKey.Name, HorizontalAlignment.Left);
        Cell("Modified", 2, FilesSortKey.Modified, HorizontalAlignment.Left);
        Cell("Size", 3, FilesSortKey.Size, HorizontalAlignment.Right);
        return g;
    }

    // file-list-layout.ts: [icon] [name *] [modified 9rem] [size 5.5rem] [actions]
    private static Grid Columns()
    {
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 0, 8, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(144) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        return g;
    }

    private UIElement Icon(FileEntry e, double size)
    {
        var s = _vm.Settings;
        if (!s.ShowIcons)
        {
            return new Border { Width = size, Height = size };
        }

        var kind = e.IsDirectory ? "Folder" : FilesPreviewRules.KindOf(e.MediaType, e.Name) == FilesPreviewKind.Image ? "Image" : "File";
        var icon = CalendarVisuals.Icon(kind, size, 1.7);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Foreground = s.ColoredIcons
            ? e.IsDirectory ? FilesVisuals.Res("Accent") : string.Equals(kind, "Image", StringComparison.Ordinal) ? CalendarVisuals.SolidBrush("#2F9E6E") : FilesVisuals.Res("Dim")
            : FilesVisuals.Res("Dim");
        return icon;
    }

    private ListViewItem Item(FileEntry e, UIElement content, CornerRadius radius)
    {
        var item = new ListViewItem { Style = FilesVisuals.Sheet("ListItem.Row"), Content = content, CornerRadius = radius, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        return Wire(item, e);
    }

    private ListViewItem Row(FileEntry e)
    {
        var g = Columns();
        g.Height = _look.FilesRowHeight;
        g.Children.Add(Icon(e, 18));
        var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement name = string.Equals(e.Key, _renaming, StringComparison.Ordinal)
            ? RenameBox(e)
            : new TextBlock { Text = e.Name, FontSize = 14, FontWeight = e.IsDirectory ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        nameCell.Children.Add(name);
        if (e.IsDirectory && FilesUi.Actions.IsStarred(e.Key))
        {
            var star = CalendarVisuals.Icon("Star", 14, 1.7);
            star.Foreground = CalendarVisuals.SolidBrush("#FBBF24");
            nameCell.Children.Add(star);
        }

        Grid.SetColumn(nameCell, 1);
        g.Children.Add(nameCell);
        Meta(g, 2, FilesUi.When(e.Modified), HorizontalAlignment.Left);
        Meta(g, 3, e.IsDirectory ? string.Empty : FilesUi.Size(e.Size), HorizontalAlignment.Right);
        var more = More(e);
        Grid.SetColumn(more, 4);
        g.Children.Add(more);
        var line = new Border { Child = g, BorderThickness = new Thickness(0, 0, 0, _look.IsBulwark ? 1 : 0), BorderBrush = FilesVisuals.Res("Line") };
        var item = Item(e, line, _look.FilesRowRadius);
        // file-views.tsx:295-310: the row's action shows on hover, and while the row is selected
        more.Opacity = 0;
        item.PointerEntered += (_, _) => more.Opacity = 1;
        item.PointerExited += (_, _) => more.Opacity = _vm.SelectedKeys.Contains(e.Key) ? 1 : 0;
        return item;
    }

    private Button More(FileEntry e)
    {
        var b = new Button { Style = FilesVisuals.Sheet("Button.HoverAction"), Content = CalendarVisuals.Icon("More", 16), Width = 28, Height = 28, CornerRadius = _look.ButtonRadius, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        FilesVisuals.Name(b, "files.more." + e.Key, "More actions for " + e.Name);
        b.Tapped += (_, t) => t.Handled = true;
        b.DoubleTapped += (_, t) => t.Handled = true;
        b.Click += (_, _) =>
        {
            if (!_vm.SelectedKeys.Contains(e.Key))
            {
                _vm.SelectFile(e.Key, false);
            }

            var p = b.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(b.ActualWidth, b.ActualHeight));
            FilesUi.ShowMenu(FilesUi.MenuFor(_vm.Location ?? string.Empty), p.X, p.Y, [.. _vm.SelectedKeys]);
        };
        return b;
    }

    // file-views.tsx:414-418: a folder is a compact h-11 item with the folder icon, the name and the more button, rounded-lg with a border
    private ListViewItem FolderItem(FileEntry e)
    {
        var g = new Grid { ColumnSpacing = 10, Height = 44, Padding = new Thickness(12, 0, 4, 0), Width = 176 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Icon(e, 18));
        FrameworkElement name = string.Equals(e.Key, _renaming, StringComparison.Ordinal) ? RenameBox(e) : new TextBlock { Text = e.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        g.Children.Add(name);
        var more = More(e);
        more.Opacity = 0;
        Grid.SetColumn(more, 2);
        g.Children.Add(more);
        return TileItem(e, g, more);
    }

    // file-views.tsx:461-492: a muted 4:3 area with the icon, the name and "size, date" under it
    private ListViewItem Tile(FileEntry e)
    {
        var stack = new Grid { Width = 176 };
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(132) });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var thumb = new Border { Background = FilesVisuals.Res("FeedbackHover"), BorderBrush = FilesVisuals.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = Icon(e, 40) };
        stack.Children.Add(thumb);
        var info = new Grid { Padding = new Thickness(10, 6, 4, 6), ColumnSpacing = 2 };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(string.Equals(e.Key, _renaming, StringComparison.Ordinal) ? RenameBox(e) : new TextBlock { Text = e.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { FilesUi.Size(e.Size), FilesUi.When(e.Modified) }.Where(x => x.Length > 0)), FontSize = 12, Foreground = FilesVisuals.Res("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(text);
        var more = More(e);
        more.Opacity = 0;
        more.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(more, 1);
        info.Children.Add(more);
        Grid.SetRow(info, 1);
        stack.Children.Add(info);
        return TileItem(e, stack, more);
    }

    private ListViewItem TileItem(FileEntry e, UIElement content, Button more)
    {
        var outline = new Border { Child = content, BorderThickness = new Thickness(1), CornerRadius = _look.FilesTileRadius, BorderBrush = FilesVisuals.Res("Line"), Background = FilesVisuals.Clear };
        var item = Item(e, outline, _look.FilesTileRadius);
        item.Margin = new Thickness(4);
        item.Tag = outline;
        item.PointerEntered += (_, _) => more.Opacity = 1;
        item.PointerExited += (_, _) => more.Opacity = _vm.SelectedKeys.Contains(e.Key) ? 1 : 0;
        return item;
    }

    private static void Meta(Grid g, int col, string text, HorizontalAlignment align)
    {
        var t = new TextBlock { Text = text, FontSize = 14, Foreground = FilesVisuals.Res("Dim"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(t, col);
        g.Children.Add(t);
    }

    private TextBox RenameBox(FileEntry e)
    {
        var box = new TextBox { Text = e.Name, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(box, "files.rename-box");
        box.Loaded += (_, _) =>
        {
            _ = box.Focus(FocusState.Programmatic);
            var dot = e.IsDirectory ? -1 : e.Name.LastIndexOf('.');
            box.Select(0, dot > 0 ? dot : e.Name.Length);
        };
        box.KeyDown += (_, k) =>
        {
            if (k.Key == VirtualKey.Enter)
            {
                var name = box.Text;
                _renaming = null;
                FilesUi.Run(() => FilesUi.Actions.RenameAsync(e.Key, name, CancellationToken.None));
                k.Handled = true;
            }
            else if (k.Key == VirtualKey.Escape)
            {
                _renaming = null;
                Render();
                k.Handled = true;
            }
        };
        return box;
    }

    private ListViewItem Wire(ListViewItem b, FileEntry e)
    {
        AutomationProperties.SetAutomationId(b, "files.row." + e.Key);
        AutomationProperties.SetName(b, e.Name);
        b.CanDrag = true;
        b.Tapped += (_, t) =>
        {
            var mods = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            Pick(e.Key, mods, shift);
            _ = Focus(FocusState.Programmatic);
            t.Handled = true;
        };
        b.DoubleTapped += (_, t) =>
        {
            FilesUi.Actions.Open(e.Key);
            t.Handled = true;
        };
        b.RightTapped += (_, t) =>
        {
            if (!_vm.SelectedKeys.Contains(e.Key))
            {
                _vm.SelectFile(e.Key, false);
            }

            var p = t.GetPosition(null);
            FilesUi.ShowMenu(FilesUi.MenuFor(_vm.Location ?? string.Empty), p.X, p.Y, [.. _vm.SelectedKeys]);
            t.Handled = true;
        };
        b.DragStarting += (_, d) =>
        {
            if (!_vm.SelectedKeys.Contains(e.Key))
            {
                _vm.SelectFile(e.Key, false);
            }

            FilesDrag.Keys = [.. _vm.SelectedKeys];
            d.Data.SetText(string.Join('\n', _vm.SelectedKeys));
        };
        b.DropCompleted += (_, _) => FilesDrag.Keys = null;
        if (e.IsDirectory)
        {
            b.AllowDrop = true;
            b.DragOver += (_, d) => d.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            b.Drop += (_, d) =>
            {
                if (FilesDrag.Keys is { Count: > 0 } keys)
                {
                    FilesUi.Run(() => FilesUi.Actions.MoveAsync(keys, e.Key, CancellationToken.None));
                    d.Handled = true;
                }
            };
        }

        return b;
    }

    /// <summary>Click selects, Ctrl toggles, Shift selects the range.</summary>
    public void Pick(string key, bool ctrl, bool shift)
    {
        if (shift)
        {
            _vm.SelectRange(key);
        }
        else
        {
            _vm.SelectFile(key, ctrl);
        }
    }

    // B-FILE-012: Enter opens, F2 renames, Delete deletes, Ctrl+A / X / C / V, Backspace goes up, Esc clears
    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var handled = true;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                handled = FilesUi.RunCommand(FilesCommandIds.Open);
                break;
            case VirtualKey.F2:
                handled = FilesUi.RunCommand(FilesCommandIds.Rename);
                break;
            case VirtualKey.Delete:
                handled = FilesUi.RunCommand(FilesCommandIds.Delete);
                break;
            case VirtualKey.Escape:
                _vm.ClearSelection();
                break;
            case VirtualKey.Back:
                if (_vm.Breadcrumb is { Count: > 0 } crumb)
                {
                    _vm.Navigate(crumb[^1].ParentKey);
                }

                break;
            case VirtualKey.A when ctrl:
                _vm.SelectAll();
                break;
            case VirtualKey.X when ctrl:
                handled = FilesUi.RunCommand(FilesCommandIds.Cut);
                break;
            case VirtualKey.C when ctrl:
                handled = FilesUi.RunCommand(FilesCommandIds.Copy);
                break;
            case VirtualKey.V when ctrl:
                handled = FilesUi.RunCommand(FilesCommandIds.Paste);
                break;
            case VirtualKey.Z when ctrl:
                handled = FilesUi.RunCommand(FilesCommandIds.Undo);
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }
}
