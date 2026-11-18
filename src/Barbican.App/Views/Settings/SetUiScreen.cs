// Mirrors: SetUiScreenFactory (the minimal screen of the M6 plan commit; set-ui replaces its body, keeping the constructor's two inputs: the navigation and the page factories).

using Barbican.App.Settings;
using Barbican.Presentation.Settings;

namespace Barbican.App.Views.Settings;

/// <summary>Page list and page host.</summary>
internal sealed class SetUiScreen : Grid
{
    private readonly ISettingsNavigation _navigation;
    private readonly IReadOnlyList<ISettingsPageFactory> _factories;
    private readonly Grid _host = new();
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);

    public SetUiScreen(ISettingsNavigation navigation, IReadOnlyList<ISettingsPageFactory> factories)
    {
        _navigation = navigation;
        _factories = factories;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var list = new StackPanel();
        foreach (var page in SettingsNav.Pages.Where(p => p.Owner != SettingsPageOwner.Later))
        {
            var id = page.Id;
            var button = new Button { Content = page.Title, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => _navigation.Open(id);
            list.Children.Add(button);
        }

        Children.Add(new ScrollViewer { Content = list });
        SetColumn(_host, 1);
        Children.Add(_host);
        _navigation.Changed += (_, _) => Show();
        Show();
    }

    public void OnShown() => FactoryOf(_navigation.Current)?.OnShown();

    private ISettingsPageFactory? FactoryOf(string id) => _factories.FirstOrDefault(f => string.Equals(f.PageId, id, StringComparison.Ordinal));

    private void Show()
    {
        var id = _navigation.Current;
        if (!_pages.ContainsKey(id))
        {
            var page = FactoryOf(id)?.Create() ?? new TextBlock { Text = (SettingsNav.Find(id)?.Title ?? id) + ": not built yet", Margin = new Thickness(24) };
            _pages[id] = page;
            _host.Children.Add(page);
        }

        foreach (var (pageId, element) in _pages)
        {
            element.Visibility = string.Equals(pageId, id, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_navigation.Anchor is { } anchor)
        {
            FactoryOf(id)?.Reveal(anchor);
        }
    }
}
