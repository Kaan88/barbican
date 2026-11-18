// Mirrors: Rust files.rs render (the list), Bulwark components/files/file-views.tsx, file-list-layout.ts (list rows and grid tiles), file-browser.tsx (click, Ctrl and Shift click, double click, right click, F2 inline rename, the keys of B-FILE-012);
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
    private readonly StackPanel _list = new() { Spacing = 1 };
    private readonly ItemsControl _gridHost = new();
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, IsHitTestVisible = false };
    private readonly Border _notice = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Top };
    private readonly Dictionary<string, FrameworkElement> _rows = new(StringComparer.Ordinal);
    private string? _renaming;

    public FilesListView()
    {
        IsTabStop = true;
        AllowDrop = true;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Grid { Children = { _list, _gridHost } }, Padding = new Thickness(8) };
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
        _gridHost.Items.Clear();
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

        _empty.Text = _vm.IsLoading ? "Loading…" : _vm.SearchText.Length > 0 ? "No files match your search" : EmptyText();
        _empty.Visibility = _vm.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (grid)
        {
            _gridHost.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' /></ItemsPanelTemplate>");
        }

        if (!grid)
        {
            _list.Children.Add(Header());
        }

        foreach (var e in _vm.Entries)
        {
            var row = grid ? Tile(e) : Row(e);
            _rows[e.Key] = row;
            if (grid)
            {
                _gridHost.Items.Add(row);
            }
            else
            {
                _list.Children.Add(row);
            }
        }

        Paint();
    }

    private string EmptyText() => _vm.Location switch
    {
        FileTrash.TrashLocation => "The trash is empty",
        FilesViewModel.FavouritesLocation => "No favourites yet. Star a folder to find it here.",
        FilesViewModel.RecentLocation => "No recent files",
        FilesViewModel.SharedLocation => "Nothing has been shared with you",
        _ => "This folder is empty. Use New to add files.",
    };

    private void Paint()
    {
        foreach (var (key, el) in _rows)
        {
            if (el is Border b)
            {
                b.Background = _vm.SelectedKeys.Contains(key) ? CalendarVisuals.Palette("Sel") : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            }
        }
    }

    private Grid Header()
    {
        var g = Columns();
        void Cell(string t, int col, FilesSortKey? key)
        {
            var b = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Content = t + (key is { } k && k == _vm.SortKey ? (_vm.SortDescending ? "  ▼" : "  ▲") : string.Empty), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 11.5, Opacity = 0.7 };
            if (key is { } sort)
            {
                b.Click += (_, _) =>
                {
                    if (_vm.SortKey == sort)
                    {
                        _vm.SortDescending = !_vm.SortDescending;
                    }
                    else
                    {
                        _vm.SortKey = sort;
                    }
                };
            }

            AutomationProperties.SetAutomationId(b, "files.sort." + t.ToLowerInvariant());
            Grid.SetColumn(b, col);
            g.Children.Add(b);
        }

        Cell("Name", 1, FilesSortKey.Name);
        Cell("Modified", 2, FilesSortKey.Modified);
        Cell("Size", 3, FilesSortKey.Size);
        Cell("Type", 4, FilesSortKey.Type);
        return g;
    }

    private static Grid Columns()
    {
        var g = new Grid { ColumnSpacing = 10, Height = 38 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
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
        if (s.ColoredIcons)
        {
            icon.Foreground = e.IsDirectory ? CalendarVisuals.Palette("Accent") : string.Equals(kind, "Image", StringComparison.Ordinal) ? CalendarVisuals.SolidBrush("#2F9E6E") : CalendarVisuals.Palette("Dim");
        }

        return icon;
    }

    private Border Row(FileEntry e)
    {
        var g = Columns();
        g.Children.Add(Icon(e, 18));
        FrameworkElement name = string.Equals(e.Key, _renaming, StringComparison.Ordinal) ? RenameBox(e) : new TextBlock { Text = e.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        g.Children.Add(name);
        Meta(g, 2, FilesUi.When(e.Modified));
        Meta(g, 3, e.IsDirectory ? string.Empty : FilesUi.Size(e.Size));
        Meta(g, 4, e.IsDirectory ? "Folder" : e.MediaType ?? string.Empty);
        return Wire(new Border { Child = g, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 0) }, e);
    }

    private Border Tile(FileEntry e)
    {
        var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(Icon(e, 44));
        FrameworkElement name = string.Equals(e.Key, _renaming, StringComparison.Ordinal) ? RenameBox(e) : new TextBlock { Text = e.Name, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120, FontSize = 12.5 };
        stack.Children.Add(name);
        return Wire(new Border { Child = stack, Width = 140, Height = 120, Margin = new Thickness(4), Padding = new Thickness(8), CornerRadius = new CornerRadius(10) }, e);
    }

    private static void Meta(Grid g, int col, string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
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

    private Border Wire(Border b, FileEntry e)
    {
        b.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
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
