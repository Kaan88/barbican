// Mirrors: Bulwark components/settings/settings-app.tsx (the page list grouped, the search box, Back and Forward, "Show advanced settings"; I-395..I-398), M6 plan section 4.1 and decision D-M6-B01: the rail, the page list with its search on the left, the page of the factory registered for its id on the right.
// Pages are built on the first visit and then only hidden; a page without a factory is drawn from its catalog rows (D-M6-B02).

using Barbican.App.Settings;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Rail;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Ui;
using Microsoft.UI.Xaml.Automation;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>Page list and page host.</summary>
internal sealed class SetUiScreen : Grid
{
    private readonly ISettingsNavigation _navigation;
    private readonly IReadOnlyList<ISettingsPageFactory> _factories;
    private readonly ISetUiViewModel _vm = SetUiUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly Grid _host = new();
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISettingsPageFactory> _generic = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly StackPanel _hits = new() { Spacing = 2, Visibility = Visibility.Collapsed };
    private readonly TextBox _search = new() { PlaceholderText = "Search settings" };
    private readonly Border _listTile = new();
    private readonly Border _surface = new();
    private readonly Button _back;
    private readonly Button _forward;
    private bool _listBuilt;
    private string? _shownPage;

    public SetUiScreen(ISettingsNavigation navigation, IReadOnlyList<ISettingsPageFactory> factories)
    {
        _navigation = navigation;
        _factories = factories;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Children.Add(new RailView());

        _back = SetUiUi.Pill("Back", () => _navigation.Back(), "set.back");
        _forward = SetUiUi.Pill("Forward", () => _navigation.Forward(), "set.forward");
        _back.MinWidth = 64;
        _forward.MinWidth = 64;
        _ = SetUiUi.Id(_search, "set.search");
        _search.TextChanged += (_, _) => _vm.SearchText = _search.Text;
        var advanced = new ToggleSwitch { OnContent = "Show advanced settings", OffContent = "Show advanced settings", IsOn = _vm.ShowAdvanced };
        _ = SetUiUi.Id(advanced, "set.advanced");
        advanced.Toggled += (_, _) => _vm.ShowAdvanced = advanced.IsOn;

        var column = new Grid { Padding = new Thickness(12), RowSpacing = 8 };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _back, _forward } });
        SetRow(_search, 1);
        column.Children.Add(_search);
        var lists = new Grid();
        lists.Children.Add(_list);
        lists.Children.Add(_hits);
        var scroll = SetUiUi.Scroll(lists);
        SetRow(scroll, 2);
        column.Children.Add(scroll);
        SetRow(advanced, 3);
        column.Children.Add(advanced);
        _listTile.Child = column;
        SetColumn(_listTile, 1);
        Children.Add(_listTile);

        _surface.Child = _host;
        SetColumn(_surface, 2);
        Children.Add(_surface);
        AutomationProperties.SetAutomationId(this, "settings.screen");

        _ = RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Collapsed && _shownPage is { } page)
                FactoryOf(page).OnHidden();
        });
        _navigation.Changed += (_, _) => Show();
        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ISetUiViewModel.SearchHits):
                    RenderHits();
                    break;
                case nameof(ISetUiViewModel.VisiblePages):
                    BuildList();
                    break;
            }
        };
        _look.PropertyChanged += (_, _) => ApplyLook();
        BuildList();
        ApplyLook();
        Show();
    }

    public void OnShown() => FactoryOf(_navigation.Current).OnShown();

    private ISettingsPageFactory FactoryOf(string id)
    {
        var registered = _factories.FirstOrDefault(f => string.Equals(f.PageId, id, StringComparison.Ordinal));
        if (registered is not null)
        {
            return registered;
        }

        if (!_generic.TryGetValue(id, out var factory))
        {
            _generic[id] = factory = SetUiPages.For(id);
        }

        return factory;
    }

    private void BuildList()
    {
        _list.Children.Clear();
        _buttons.Clear();
        string? group = null;
        foreach (var page in _vm.VisiblePages)
        {
            if (!string.Equals(group, page.Group, StringComparison.Ordinal))
            {
                group = page.Group;
                var heading = group switch { "mail" => "Mail", "apps" => "Apps", "bottom" => "More", _ => string.Empty };
                if (heading.Length > 0)
                {
                    _list.Children.Add(new TextBlock { Text = heading.ToUpperInvariant(), Style = (Style)Application.Current.Resources["Text.Label"], Margin = new Thickness(8, 12, 0, 4) });
                }
            }

            var id = page.Id;
            var button = new Button
            {
                Content = page.Title,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = AppResources.Brush("Bg"),
                BorderThickness = new Thickness(0),
                Foreground = AppResources.Brush("Body"),
                CornerRadius = new CornerRadius(8),
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
            };
            _ = SetUiUi.Id(button, "set.page." + id);
            button.Click += (_, _) => _navigation.Open(id);
            _buttons[id] = button;
            _list.Children.Add(button);
        }

        Mark();
        _listBuilt = true;
    }

    private void RenderHits()
    {
        _hits.Children.Clear();
        var hits = _vm.SearchHits;
        var searching = !string.IsNullOrWhiteSpace(_vm.SearchText);
        _hits.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        if (searching && hits.Count == 0)
        {
            _hits.Children.Add(SetUiUi.Meta("No setting matches."));
        }

        foreach (var hit in hits.Take(40))
        {
            var page = SettingsNav.Find(hit.PageId)?.Title ?? hit.PageId;
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(SetUiUi.Body(hit.Label));
            stack.Children.Add(SetUiUi.Meta(page));
            var button = new Button { Content = stack, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = AppResources.Brush("Bg"), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8) };
            _ = SetUiUi.Id(button, "set.hit." + hit.RowId);
            var pageId = hit.PageId;
            var rowId = hit.RowId;
            button.Click += (_, _) => _navigation.Open(pageId, rowId.Length > 0 ? rowId : null);
            _hits.Children.Add(button);
        }
    }

    private void Mark()
    {
        foreach (var (id, button) in _buttons)
        {
            var on = string.Equals(id, _navigation.Current, StringComparison.Ordinal);
            button.Background = on ? AppResources.Brush("Sel") : AppResources.Brush("Bg");
            button.Foreground = on ? AppResources.Brush("SelText") : AppResources.Brush("Body");
        }

        _back.IsEnabled = _navigation.CanGoBack;
        _forward.IsEnabled = _navigation.CanGoForward;
    }

    private void ApplyLook()
    {
        var bento = _look.IsBento;
        _listTile.Background = bento ? _look.TileBrush : null;
        _listTile.BorderBrush = bento ? AppResources.Brush("Line") : null;
        _listTile.BorderThickness = _look.PaneBorder;
        _listTile.CornerRadius = _look.PaneRadius;
        _listTile.Margin = bento ? new Thickness(4) : new Thickness(0);
        _surface.Background = bento ? _look.TileBrush : _look.SurfaceBrush;
        _surface.BorderBrush = bento ? AppResources.Brush("Line") : null;
        _surface.BorderThickness = _look.PaneBorder;
        _surface.CornerRadius = bento ? _look.PaneRadius : _look.SurfaceRadius;
        _surface.Margin = bento ? new Thickness(4) : new Thickness(0);
    }

    private void Show()
    {
        var id = _navigation.Current;
        if (_shownPage is { } previous && !string.Equals(previous, id, StringComparison.Ordinal))
            FactoryOf(previous).OnHidden();
        _shownPage = id;
        var factory = FactoryOf(id);
        if (!_pages.ContainsKey(id))
        {
            var page = factory.Create();
            _pages[id] = page;
            _host.Children.Add(page);
        }

        foreach (var (pageId, element) in _pages)
        {
            element.Visibility = string.Equals(pageId, id, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_listBuilt)
        {
            Mark();
        }

        if (Visibility == Visibility.Visible) factory.OnShown();
        if (_navigation.Anchor is { } anchor)
        {
            factory.Reveal(anchor);
        }
    }
}
