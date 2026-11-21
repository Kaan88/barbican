// Mirrors: Bulwark components/settings/settings-app.tsx (the page list grouped, the search box, Back and Forward, "Show advanced settings"; I-395..I-398), M6 plan section 4.1 and decision D-M6-B01: the rail, the page list with its search on the left, the page of the factory registered for its id on the right.
// Pages are built on the first visit and then only hidden; a page without a factory is drawn from its catalog rows (D-M6-B02).
// Bulwark frame (settings-app.tsx:1213-1360), Bulwark style only: nav column 256 px on bg-secondary with a border-e; an h-14 head with the border-b holding the "Back to mail" ghost button (:1239-1253);
// the search field h-8 with its icon (ui/toolbar.tsx:55-110); groups separated by a rule (mx-1 my-2 border-t) and headed "Mail" and "Apps" in 11 px semibold muted (:1291-1302); each page a row py-2 ps-3 pe-3 text-sm
// gap-2.5 with a 16 px icon, rounded-md, the open one bg-accent text-accent-foreground font-medium, hover bg-muted (:1305-1330); search results listed under their page as ps-9 text-xs muted rows (:1331-1345);
// the foot with the border-t and the Advanced switch (settings-section.tsx:157-171). Bento and Nordic keep the tile with the plain list. ST-01.

using Barbican.App.Controls;
using Barbican.App.Settings;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Rail;
using Barbican.Core.Storage;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Ui;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;
using System.Text.Json.Nodes;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>Page list and page host.</summary>
internal sealed class SetUiScreen : Grid
{
    // The icon of each page in the nav (settings-tab-icons.ts:30-54), the nearest of the app's line icons.
    private static readonly Dictionary<string, string> NavIcons = new(StringComparer.Ordinal)
    {
        [SettingsPageIds.Account] = "User",
        [SettingsPageIds.Security] = "Key",
        [SettingsPageIds.Privacy] = "Shield",
        [SettingsPageIds.Notifications] = "Bell",
        [SettingsPageIds.General] = "Settings",
        [SettingsPageIds.Appearance] = "Spark",
        [SettingsPageIds.Accessibility] = "Eye",
        [SettingsPageIds.MessageList] = "List",
        [SettingsPageIds.Reading] = "Book",
        [SettingsPageIds.Composing] = "Pen",
        [SettingsPageIds.Identities] = "Send",
        [SettingsPageIds.Templates] = "Tpl",
        [SettingsPageIds.Organize] = "Folder",
        [SettingsPageIds.Filters] = "Filter",
        [SettingsPageIds.Vacation] = "Sun",
        [SettingsPageIds.Recover] = "Undo",
        [SettingsPageIds.Assistant] = "Spark",
        [SettingsPageIds.Calendar] = "Cal",
        [SettingsPageIds.Contacts] = "Users",
        [SettingsPageIds.Files] = "File",
        [SettingsPageIds.Addons] = "Package",
        [SettingsPageIds.Backup] = "Archive",
        [SettingsPageIds.Troubleshooting] = "Tool",
        [SettingsPageIds.About] = "Info",
    };

    private const string AssistantPluginId = "assistant";

    private readonly ISettingsNavigation _navigation;
    private readonly IReadOnlyList<ISettingsPageFactory> _factories;
    private readonly ISetUiViewModel _vm = SetUiUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly Grid _host = new();
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISettingsPageFactory> _generic = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Button Button, SIcon? Icon, TextBlock Text)> _buttons = new(StringComparer.Ordinal);
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly StackPanel _hits = new() { Spacing = 2, Visibility = Visibility.Collapsed };
    private readonly TextBox _search = new() { PlaceholderText = "Search settings" };
    private readonly Border _listTile = new();
    private readonly Border _surface = new();
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _exit;
    private readonly Border _head = new();
    private readonly Border _foot = new();
    private readonly SIcon _searchIcon = new() { Kind = "Search", Size = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly ColumnDefinition _navColumn = new();
    private bool _listBuilt;
    private string? _shownPage;
    private ShellStyle _style;

    public SetUiScreen(ISettingsNavigation navigation, IReadOnlyList<ISettingsPageFactory> factories)
    {
        _navigation = navigation;
        _factories = factories;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(_navColumn);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Children.Add(new RailView());

        _back = HistoryButton("Back", "Back", () => _navigation.Back(), "set.back");
        _forward = HistoryButton("ChevronRight", "Forward", () => _navigation.Forward(), "set.forward");
        _exit = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 32, Padding = new Thickness(8, 0, 12, 0) };
        _exit.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new SIcon { Kind = "Back", Size = 16 }, new TextBlock { Text = "Back to mail", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center } },
        };
        _exit.Click += (_, _) => _ = ViewHost.Get<ShellViewModel>().TryActivate(AppId.Mail);
        _ = SetUiUi.Id(_exit, "set.exit");
        AutomationProperties.SetName(_exit, "Back to mail");

        _ = SetUiUi.Id(_search, "set.search");
        _search.TextChanged += (_, _) => _vm.SearchText = _search.Text;
        var advanced = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0, IsOn = _vm.ShowAdvanced, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(advanced, "Show advanced settings");
        _ = SetUiUi.Id(advanced, "set.advanced");
        advanced.Toggled += (_, _) => _vm.ShowAdvanced = advanced.IsOn;
        var advancedRow = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
        advancedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        advancedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        advancedRow.Children.Add(new TextBlock { Text = "Show advanced settings", FontSize = 14, Foreground = AppResources.Brush("Text"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        SetColumn(advanced, 1);
        advancedRow.Children.Add(advanced);

        var headRow = new Grid { ColumnSpacing = 4, Padding = new Thickness(8, 0, 8, 0) };
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headRow.Children.Add(_back);
        SetColumn(_forward, 1);
        headRow.Children.Add(_forward);
        SetColumn(_exit, 2);
        headRow.Children.Add(_exit);
        _head.Child = headRow;

        var searchBox = new Grid { Margin = new Thickness(12, 4, 12, 4) };
        searchBox.Children.Add(_search);
        searchBox.Children.Add(_searchIcon);

        var column = new Grid();
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.Children.Add(_head);
        SetRow(searchBox, 1);
        column.Children.Add(searchBox);
        var lists = new Grid { Margin = new Thickness(12, 4, 12, 4) };
        lists.Children.Add(_list);
        lists.Children.Add(_hits);
        var scroll = SetUiUi.Scroll(lists);
        SetRow(scroll, 2);
        column.Children.Add(scroll);
        _foot.Child = advancedRow;
        SetRow(_foot, 3);
        column.Children.Add(_foot);
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
        if (ViewHost.Find<IPrefsStore>() is { } prefs)
        {
            // The AI assistant page follows the bundled add-on: turned off in Add-ons, its entry leaves the list.
            prefs.Changed += (_, _) => _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_listBuilt && AssistantListed() != _assistantShown) BuildList();
            });
        }

        _style = _look.Style;
        _look.PropertyChanged += (_, _) =>
        {
            ApplyLook();
            if (_look.Style == _style) return;
            _style = _look.Style;
            BuildList();
            RenderHits();
        };
        BuildList();
        ApplyLook();
        Show();
    }

    private bool _assistantShown;

    public void OnShown() => FactoryOf(_navigation.Current).OnShown();

    private static Button HistoryButton(string icon, string name, Action click, string id)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 32, Height = 32, Padding = new Thickness(0), Content = new SIcon { Kind = icon, Size = 16 } };
        button.Click += (_, _) => click();
        AutomationProperties.SetName(button, name);
        return SetUiUi.Id(button, id);
    }

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

    /// <summary>True unless the bundled assistant add-on is switched off (the disabled list of the prefs, Rust main.rs:455).</summary>
    private static bool AssistantListed()
    {
        var prefs = ViewHost.Find<IPrefsStore>();
        if (prefs is null || prefs.Current.Values["disabledPlugins"] is not JsonArray off)
        {
            return true;
        }

        return !off.Any(n => n is JsonValue v && v.TryGetValue<string>(out var s) && string.Equals(s, AssistantPluginId, StringComparison.Ordinal));
    }

    private void BuildList()
    {
        _list.Children.Clear();
        _buttons.Clear();
        var bulwark = _look.IsBulwark;
        _assistantShown = AssistantListed();
        string? group = null;
        foreach (var page in _vm.VisiblePages)
        {
            if (string.Equals(page.Id, SettingsPageIds.Assistant, StringComparison.Ordinal) && !_assistantShown && !string.Equals(_navigation.Current, page.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(group, page.Group, StringComparison.Ordinal))
            {
                if (bulwark && group is not null)
                {
                    _list.Children.Add(new Border { Height = 1, Margin = new Thickness(4, 8, 4, 8), Background = AppResources.Brush("Line") });
                }

                group = page.Group;
                var heading = group switch { "mail" => "Mail", "apps" => "Apps", "bottom" => bulwark ? string.Empty : "More", _ => string.Empty };
                if (heading.Length > 0)
                {
                    _list.Children.Add(bulwark
                        ? new TextBlock { Text = heading, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Dim"), Margin = new Thickness(12, 10, 0, 4) }
                        : new TextBlock { Text = heading.ToUpperInvariant(), Style = (Style)Application.Current.Resources["Text.Label"], Margin = new Thickness(8, 12, 0, 4) });
                }
            }

            _list.Children.Add(NavRow(page));
        }

        Mark();
        _listBuilt = true;
    }

    private Button NavRow(SettingsPageInfo page, bool register = true)
    {
        var bulwark = _look.IsBulwark;
        var id = page.Id;
        var text = new TextBlock { Text = page.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = bulwark ? 14 : 13 };
        SIcon? icon = null;
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        if (bulwark)
        {
            // settings-app.tsx:1305-1330: py-2 pe-3 ps-3, gap-2.5, icon 16, rounded-md, hover bg-muted
            icon = new SIcon { Kind = NavIcons.GetValueOrDefault(id, "Settings"), Size = 16, Foreground = AppResources.Brush("Dim") };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(icon);
            row.Children.Add(text);
            button.Content = row;
            button.Style = _look.MenuItemStyle;
            button.CornerRadius = new CornerRadius(_look.Metrics.ButtonRadius);
            button.Padding = new Thickness(12, 8, 12, 8);
            button.MinHeight = 36;
        }
        else
        {
            button.Content = text;
            button.Background = AppResources.Brush("Bg");
            button.BorderThickness = new Thickness(0);
            button.Foreground = AppResources.Brush("Body");
            button.CornerRadius = new CornerRadius(8);
            button.Height = 34;
            button.Padding = new Thickness(12, 0, 12, 0);
        }

        AutomationProperties.SetName(button, page.Title);
        _ = SetUiUi.Id(button, "set.page." + id);
        button.Click += (_, _) => _navigation.Open(id);
        if (register) _buttons[id] = (button, icon, text);
        return button;
    }

    private void RenderHits()
    {
        _hits.Children.Clear();
        var hits = _vm.SearchHits;
        var searching = !string.IsNullOrWhiteSpace(_vm.SearchText);
        _hits.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        if (!searching)
        {
            return;
        }

        if (hits.Count == 0)
        {
            _hits.Children.Add(new TextBlock { Text = "No matching settings", FontSize = 14, Foreground = AppResources.Brush("Dim"), TextAlignment = TextAlignment.Center, Margin = new Thickness(12, 24, 12, 24), TextWrapping = TextWrapping.Wrap });
            return;
        }

        var bulwark = _look.IsBulwark;
        foreach (var byPage in hits.Take(40).GroupBy(h => h.PageId, StringComparer.Ordinal))
        {
            if (SettingsNav.Find(byPage.Key) is { } info)
            {
                _hits.Children.Add(NavRow(info, register: false));
            }

            foreach (var hit in byPage.Take(6))
            {
                var stack = new StackPanel { Spacing = 1 };
                if (bulwark)
                {
                    stack.Children.Add(new TextBlock { Text = hit.Label, FontSize = 12, Foreground = AppResources.Brush("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
                }
                else
                {
                    stack.Children.Add(SetUiUi.Body(hit.Label));
                    stack.Children.Add(SetUiUi.Meta(SettingsNav.Find(hit.PageId)?.Title ?? hit.PageId));
                }

                var button = new Button { Content = stack, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                if (bulwark)
                {
                    button.Style = _look.MenuItemStyle;
                    button.CornerRadius = new CornerRadius(_look.Metrics.ButtonRadius);
                    button.Padding = new Thickness(36, 6, 12, 6);
                }
                else
                {
                    button.Background = AppResources.Brush("Bg");
                    button.BorderThickness = new Thickness(0);
                    button.CornerRadius = new CornerRadius(8);
                }

                _ = SetUiUi.Id(button, "set.hit." + hit.RowId);
                var pageId = hit.PageId;
                var rowId = hit.RowId;
                button.Click += (_, _) => _navigation.Open(pageId, rowId.Length > 0 ? rowId : null);
                _hits.Children.Add(button);
            }
        }
    }

    private void Mark()
    {
        var bulwark = _look.IsBulwark;
        foreach (var (id, parts) in _buttons)
        {
            var on = string.Equals(id, _navigation.Current, StringComparison.Ordinal);
            if (bulwark)
            {
                parts.Button.Background = on ? AppResources.Brush("FeedbackSelected") : _look.Transparent;
                var fg = on ? AppResources.Brush("FeedbackSelectedFg") : AppResources.Brush("Text");
                parts.Button.Foreground = fg;
                parts.Text.Foreground = fg;
                parts.Text.FontWeight = on ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.Normal;
                if (parts.Icon is not null)
                {
                    parts.Icon.Foreground = on ? AppResources.Brush("FeedbackSelectedFg") : AppResources.Brush("Dim");
                }
            }
            else
            {
                parts.Button.Background = on ? AppResources.Brush("Sel") : AppResources.Brush("Bg");
                parts.Button.Foreground = on ? AppResources.Brush("SelText") : AppResources.Brush("Body");
            }
        }

        _back.IsEnabled = _navigation.CanGoBack;
        _forward.IsEnabled = _navigation.CanGoForward;
    }

    private void ApplyLook()
    {
        var bulwark = _look.IsBulwark;
        var bento = _look.IsBento && !bulwark;
        _navColumn.Width = new GridLength(_look.SetNavWidth);
        _listTile.Background = bulwark ? _look.SidebarBrush : bento ? _look.TileBrush : null;
        _listTile.BorderBrush = bulwark || bento ? AppResources.Brush("Line") : null;
        _listTile.BorderThickness = bulwark ? new Thickness(0, 0, 1, 0) : _look.PaneBorder;
        _listTile.CornerRadius = bulwark ? new CornerRadius(0) : _look.PaneRadius;
        _listTile.Margin = bento ? new Thickness(4) : new Thickness(0);
        _surface.Background = bulwark ? AppResources.Brush("Bg") : bento ? _look.TileBrush : _look.SurfaceBrush;
        _surface.BorderBrush = bento ? AppResources.Brush("Line") : null;
        _surface.BorderThickness = bulwark ? new Thickness(0) : _look.PaneBorder;
        _surface.CornerRadius = bulwark ? new CornerRadius(0) : bento ? _look.PaneRadius : _look.SurfaceRadius;
        _surface.Margin = bento ? new Thickness(4) : new Thickness(0);

        // the head with its border-b (h-14) and the foot with its border-t exist in Bulwark only; the older styles pad the controls on the tile
        _head.Height = bulwark ? 56 : double.NaN;
        _head.BorderBrush = AppResources.Brush("Line");
        _head.BorderThickness = bulwark ? new Thickness(0, 0, 0, 1) : new Thickness(0);
        _head.Padding = bulwark ? new Thickness(0) : new Thickness(12, 12, 12, 0);
        _exit.Visibility = bulwark ? Visibility.Visible : Visibility.Collapsed;
        _foot.BorderBrush = AppResources.Brush("Line");
        _foot.BorderThickness = bulwark ? new Thickness(0, 1, 0, 0) : new Thickness(0);
        _foot.Padding = bulwark ? new Thickness(4, 4, 4, 4) : new Thickness(4, 4, 4, 8);
        _search.MinHeight = bulwark ? 32 : 0;
        _search.Height = bulwark ? 32 : double.NaN;
        _search.Padding = bulwark ? new Thickness(32, 4, 8, 4) : new Thickness(10, 6, 10, 6);
        _search.FontSize = 14;
        _searchIcon.Visibility = bulwark ? Visibility.Visible : Visibility.Collapsed;
        _searchIcon.Foreground = AppResources.Brush("Dim");
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
