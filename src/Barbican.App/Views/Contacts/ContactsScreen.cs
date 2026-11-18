// Mirrors: M5 plan section 4.12 and 6.2 step 1 (the Contacts app in the shell: rail | sidebar | people list | detail, built on the first visit by ContactsScreenFactory and then only hidden; M4.md for how the calendar was hosted); spec/layout/contacts.md (Bento: a tile per region; Nordic: list and detail share one rounded surface, a hairline between);
// design BNContacts; Rust app.slint:4749-5109. The list is 330 (Nordic 340) wide; the sidebar width and its handle are the mail screen's (PanesViewModel).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Actions;
using Barbican.App.Views.Rail;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Contacts;

/// <summary>The Contacts app's screen.</summary>
public sealed class ContactsScreen : UserControl
{
    private static ContactsScreen? _current;
    private readonly ContactsViewModel _vm = ContactsUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly PanesViewModel _panes = ViewHost.Get<PanesViewModel>();
    private readonly Grid _root = new();
    private readonly RailView _rail = new();
    private readonly ContactsSidebarView _sidebar = new();
    private readonly PaneHandle _handle = new() { Column = "sidebar" };
    private readonly Border _surface = new();
    private readonly Grid _content = new();
    private readonly Border _listTile = new();
    private readonly Border _detailTile = new();
    private readonly Border _divider = new() { Width = 1 };
    private readonly ContactsListView _list = new();
    private readonly ContactsDetailView _detail = new();
    private string? _loadedFor;

    public ContactsScreen()
    {
        _current = this;
        Build();
        _look.PropertyChanged += (_, _) => ApplyLook();
        _panes.PropertyChanged += (_, _) => ApplyLook();
        SizeChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    /// <summary>The screen on show, or null before the first visit.</summary>
    public static ContactsScreen? Current => _current;

    public ContactsListView List => _list;

    public ContactsDetailView Detail => _detail;

    public ContactsSidebarView Sidebar => _sidebar;

    /// <summary>The screen is shown (the rail opened Contacts): loads the login's contacts the first time and again after a login change.</summary>
    public void OnShown()
    {
        var key = _vm.AccountKey;
        if (key is not null && (!string.Equals(_loadedFor, key, StringComparison.Ordinal) || !_vm.IsLoadedFor(key)) && !_vm.IsLoading)
        {
            _loadedFor = key;
            ContactsUi.Run(() => _vm.RefreshAsync(CancellationToken.None));
        }
    }

    /// <summary>Puts the cursor in the search field ("/").</summary>
    public void FocusSearch() => _ = _list.SearchBox.Focus(FocusState.Programmatic);

    private void Build()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(_rail);
        Grid.SetColumn(_sidebar, 1);
        _root.Children.Add(_sidebar);
        _handle.Target = _sidebar;
        Grid.SetColumn(_handle, 1);
        AutomationProperties.SetAutomationId(_handle, "pane.contacts-sidebar");
        _root.Children.Add(_handle);

        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _listTile.Child = _list;
        _detailTile.Child = _detail;
        Grid.SetColumn(_divider, 1);
        Grid.SetColumn(_detailTile, 2);
        _content.Children.Add(_listTile);
        _content.Children.Add(_divider);
        _content.Children.Add(_detailTile);
        _surface.Child = _content;
        Grid.SetColumn(_surface, 2);
        _root.Children.Add(_surface);
        Content = _root;
        AutomationProperties.SetAutomationId(this, "contacts");
        AutomationProperties.SetName(this, "Contacts");
    }

    private void ApplyLook()
    {
        var layout = _look.Layout;
        var band = false;
#if WINDOWS
        band = true;
#endif
        var pad = _look.WindowPadding;
        Padding = band ? pad : new Thickness(pad.Left, pad.Bottom, pad.Right, pad.Bottom);
        _root.ColumnSpacing = _look.Gap;
        _sidebar.Visibility = layout.SidebarShown ? Visibility.Visible : Visibility.Collapsed;
        _handle.Visibility = _sidebar.Visibility;
        _sidebar.Width = _panes.SidebarWidth > 0 ? _panes.SidebarWidth : _look.SidebarWidth;
        var bento = _look.IsBento;
        _surface.Background = bento ? null : _look.TileBrush;
        _surface.BorderBrush = bento ? null : ContactsUi.Palette("Line");
        _surface.BorderThickness = new Thickness(bento ? 0 : 1);
        _surface.CornerRadius = bento ? new CornerRadius(0) : _look.PaneRadius;
        _content.ColumnSpacing = bento ? _look.Gap : 0;
        foreach (var tile in new[] { _listTile, _detailTile })
        {
            tile.Background = bento ? _look.TileBrush : null;
            tile.BorderBrush = bento ? ContactsUi.Palette("Line") : null;
            tile.BorderThickness = new Thickness(bento ? 1 : 0);
            tile.CornerRadius = bento ? new CornerRadius(26) : new CornerRadius(0);
        }

        _listTile.Padding = bento ? new Thickness(10, 18, 10, 0) : new Thickness(14, 20, 14, 0);
        _listTile.Width = bento ? 330 : 340;
        _divider.Visibility = bento ? Visibility.Collapsed : Visibility.Visible;
        _divider.Background = ContactsUi.Palette("Line");
        // a narrow window shows the list only until a contact is open, then the detail alone
        var narrow = _look.WindowWidth > 0 && _look.WindowWidth < 900;
        _detailTile.Visibility = narrow && _vm.OpenId is null ? Visibility.Collapsed : Visibility.Visible;
        _listTile.Visibility = narrow && _vm.OpenId is not null ? Visibility.Collapsed : Visibility.Visible;
        _divider.Visibility = bento || narrow ? Visibility.Collapsed : Visibility.Visible;
        if (narrow)
        {
            _listTile.Width = double.NaN;
            _listTile.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            _listTile.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }
}
