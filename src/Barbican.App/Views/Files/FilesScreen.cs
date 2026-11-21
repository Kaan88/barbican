// Mirrors: M5 plan section 6.4 (the Files app beside the Mail screen: rail | sidebar | toolbar and list | details panel; the screen is built once by FilesScreenFactory and only hidden after); Bulwark components/files/files-app.tsx; design Files restyle (B-FILE-017: a tile per region in Bento, one surface in Nordic).
// Bulwark (WP-14): the sidebar and details panel sit on the shared secondary and background fills with a hairline between regions.
// The preview modal sits over the whole screen; dialogs and menus use the window's own hosts (in-window, ADR 0007).

using Barbican.App.Shell;
using Microsoft.UI.Xaml.Automation;
using Barbican.App.Theme;
using Barbican.App.Views.Rail;
using Barbican.Core.Accounts;
using Barbican.Presentation.Files;

namespace Barbican.App.Views.Files;

/// <summary>The Files app's screen.</summary>
public sealed class FilesScreen : UserControl
{
    private readonly FilesViewModel _vm = FilesUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly FilesSidebarView _sidebar = new();
    private readonly FilesToolbarView _toolbar = new();
    private readonly FilesListView _list = new();
    private readonly FilesDetailsView _details = new();
    private readonly Border _surface = new();
    private readonly Border _detailsTile = new();
    private readonly Border _sidebarTile = new();
    private bool _loaded;

    public FilesScreen()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(new RailView());
        _sidebarTile.Child = _sidebar;
        Grid.SetColumn(_sidebarTile, 1);
        root.Children.Add(_sidebarTile);
        var column = new Grid();
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.Children.Add(_toolbar);
        Grid.SetRow(_list, 1);
        column.Children.Add(_list);
        _surface.Child = column;
        Grid.SetColumn(_surface, 2);
        root.Children.Add(_surface);
        _detailsTile.Child = _details;
        Grid.SetColumn(_detailsTile, 3);
        root.Children.Add(_detailsTile);
        var all = new Grid();
        all.Children.Add(root);
        var preview = new FilesPreviewView();
        all.Children.Add(preview);
        Preview = preview;
        Content = all;
        AutomationProperties.SetAutomationId(this, "files.screen");
        _vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(FilesViewModel.ShowDetails), StringComparison.Ordinal))
            {
                ApplyLook();
            }
        };
        _look.PropertyChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    /// <summary>The preview modal (automation reads it).</summary>
    public FilesPreviewView Preview { get; }

    /// <summary>The list.</summary>
    public FilesListView List => _list;

    /// <summary>The screen is shown (the rail opened Files): the first time loads the login's files.</summary>
    public void OnShown()
    {
        if (_vm.AccountKey.Length == 0 && (ViewHost.Find<IAccountManager>()?.Active?.Key ?? FilesScreenFactory.SampleKey) is { } key)
        {
            _vm.AccountKey = key;
        }

        if (!_loaded)
        {
            _loaded = true;
            FilesUi.Run(() => _vm.RefreshAsync(CancellationToken.None));
        }

        _list.Render();
        _sidebar.Render();
    }

    private void ApplyLook()
    {
        var bento = _look.IsBento && !_look.IsBulwark;
        foreach (var tile in new[] { _sidebarTile, _surface, _detailsTile })
        {
            tile.Background = bento ? _look.TileBrush : null;
            tile.BorderBrush = bento || _look.IsBulwark ? Calendar.CalendarVisuals.Palette("Line") : null;
            tile.BorderThickness = _look.PaneBorder;
            tile.CornerRadius = _look.PaneRadius;
            tile.Margin = bento ? new Thickness(4) : new Thickness(0);
        }

        // Bulwark: the sidebar is bg-secondary with a border on its right (files-sidebar.tsx:287), the details panel bg-background with a border on its left (file-details-panel.tsx:90)
        if (_look.IsBulwark)
        {
            _sidebarTile.Background = FilesVisuals.Res("Side");
            _sidebarTile.BorderThickness = new Thickness(0, 0, 1, 0);
            _detailsTile.BorderThickness = new Thickness(1, 0, 0, 0);
            _surface.BorderThickness = new Thickness(0);
        }

        _surface.Background = _look.SurfaceBrush;
        _detailsTile.Visibility = _vm.ShowDetails ? Visibility.Visible : Visibility.Collapsed;
    }
}
