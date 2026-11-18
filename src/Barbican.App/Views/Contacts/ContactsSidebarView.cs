// Mirrors: Rust app.slint:4749-4870 (the contacts sidebar), spec/layout/contacts.md (Bento: tiles; Nordic: dot rows on the ground), Bulwark components/contacts/contacts-sidebar.tsx (New contact, All contacts, address books, groups with +, categories, Directory, Duplicates);
// design BNContacts; I-345..I-351, I-066 (the account scope is the active login: one login at a time, as the calendar). Rows open the places of ContactsViewModel; the right click and the "..." button open MENU-BOOK, MENU-GROUP and MENU-CATEGORY (con-ui's providers).

using Barbican.App.Controls;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Contacts;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Contacts;

/// <summary>The Contacts sidebar.</summary>
public sealed class ContactsSidebarView : UserControl
{
    private readonly ContactsViewModel _vm = ContactsUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly StackPanel _stack = new() { Spacing = 12 };
    private readonly Button _new;
    private readonly List<Border> _tiles = [];

    public ContactsSidebarView()
    {
        _new = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Primary"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new SIcon { Kind = "Plus", Size = 16, StrokeWidth = 2.2 }, new TextBlock { Text = "New contact", VerticalAlignment = VerticalAlignment.Center } } },
        };
        AutomationProperties.SetAutomationId(_new, "contacts.new");
        AutomationProperties.SetName(_new, "New contact");
        _new.Click += (_, _) => _ = ContactsUi.RunCommand(ContactsCommandIds.New, _vm.Place == ContactsPlace.Book ? _vm.PlaceId : null);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stack, Padding = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(this, "contacts.sidebar");
        AutomationProperties.SetName(this, "Contacts");
        _vm.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Render);
        _look.PropertyChanged += (_, _) => Render();
        Render();
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
            tile.BorderBrush = bento ? ContactsUi.Palette("Line") : null;
            tile.BorderThickness = new Thickness(bento ? 1 : 0);
            tile.CornerRadius = bento ? _look.PaneRadius : new CornerRadius(0);
            tile.Padding = bento ? new Thickness(12) : new Thickness(4, 0, 4, 0);
        }
    }

    /// <summary>Redraws the sections from the model.</summary>
    public void Render()
    {
        _tiles.Clear();
        _stack.Children.Clear();
        _stack.Children.Add(_new);
        var places = new StackPanel { Spacing = 2 };
        places.Children.Add(Row("contacts.place.all", "All contacts", _vm.Contacts.Count, _vm.Place == ContactsPlace.All, () => _vm.ShowPlace(ContactsPlace.All, null), null, null));
        if (_vm.DuplicateSetCount > 0)
        {
            places.Children.Add(Row("contacts.place.duplicates", "Duplicates", _vm.DuplicateSetCount, _vm.Place == ContactsPlace.Duplicates, () => _vm.ShowPlace(ContactsPlace.Duplicates, null), null, null));
        }

        if (_vm.DirectoryAvailable)
        {
            places.Children.Add(Row("contacts.place.directory", "Directory", null, _vm.Place == ContactsPlace.Directory, () => _vm.ShowPlace(ContactsPlace.Directory, null), null, null));
        }

        AddTile(places);

        var books = new StackPanel { Spacing = 2 };
        books.Children.Add(Section("Address books", "contacts.books.add", "New address book", () => ContactsUi.RunCommand(ContactsCommandIds.BookNew)));
        foreach (var b in _vm.Books)
        {
            var id = b.Id!;
            books.Children.Add(Row("contacts.book." + id, b.Name + (b.IsDefault ? " (default)" : string.Empty), b.Count, _vm.Place == ContactsPlace.Book && string.Equals(_vm.PlaceId, id, StringComparison.Ordinal), () => _vm.ShowPlace(ContactsPlace.Book, id), (x, y) => ContactsUi.ShowMenu(ContactsMenuIds.Book, x, y, new ContactMenuTarget(_vm.AccountKey ?? string.Empty, [], BookId: id)), "More"));
        }

        AddTile(books);

        var groups = new StackPanel { Spacing = 2 };
        groups.Children.Add(Section("Groups", "contacts.groups.add", "New group", () => ContactsUi.RunCommand(ContactsCommandIds.NewGroup)));
        foreach (var g in _vm.Groups)
        {
            var id = g.Id!;
            groups.Children.Add(Row("contacts.group." + id, g.Name, g.Count, _vm.Place == ContactsPlace.Group && string.Equals(_vm.PlaceId, id, StringComparison.Ordinal), () => _vm.ShowPlace(ContactsPlace.Group, id), (x, y) => ContactsUi.ShowMenu(ContactsMenuIds.Group, x, y, new ContactMenuTarget(_vm.AccountKey ?? string.Empty, [], GroupId: id)), "More"));
        }

        if (_vm.Groups.Count == 0)
        {
            groups.Children.Add(Note("No groups yet."));
        }

        AddTile(groups);

        if (_vm.Categories.Count > 0)
        {
            var categories = new StackPanel { Spacing = 2 };
            categories.Children.Add(Section("Categories", null, null, null));
            foreach (var c in _vm.Categories)
            {
                var name = c.Name;
                categories.Children.Add(Row("contacts.category." + name, name, c.Count, _vm.Place == ContactsPlace.Category && string.Equals(_vm.PlaceId, name, StringComparison.Ordinal), () => _vm.ShowPlace(ContactsPlace.Category, name), (x, y) => ContactsUi.ShowMenu(ContactsMenuIds.Category, x, y, new ContactMenuTarget(_vm.AccountKey ?? string.Empty, [], Category: name)), "Tag"));
            }

            AddTile(categories);
        }

        ApplyLook();
    }

    private void AddTile(UIElement content)
    {
        var tile = new Border { Child = content };
        _tiles.Add(tile);
        _stack.Children.Add(tile);
    }

    private static TextBlock Note(string text) => new() { Text = text, FontSize = 12.5, Foreground = ContactsUi.Palette("Faint"), Margin = new Thickness(2, 2, 0, 2) };

    private static Grid Section(string title, string? plusId, string? plusName, Func<bool>? plus)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4), ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        grid.Children.Add(new TextBlock { Text = title, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = ContactsUi.Palette("Dim"), VerticalAlignment = VerticalAlignment.Center });
        if (plusId is not null && plus is not null)
        {
            var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 24, Height = 24, CornerRadius = new CornerRadius(8), Content = new SIcon { Kind = "Plus", Size = 14, StrokeWidth = 2 } };
            AutomationProperties.SetAutomationId(button, plusId);
            AutomationProperties.SetName(button, plusName ?? title);
            button.Click += (_, _) => plus();
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
        }

        return grid;
    }

    private static Grid Row(string automationId, string name, int? count, bool selected, Action click, Action<double, double>? menu, string? moreIcon)
    {
        var grid = new Grid { Height = 32, ColumnSpacing = 8, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0, 4, 0), Background = selected ? ContactsUi.Palette("Sel") : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = name, FontSize = 13, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, Foreground = ContactsUi.Palette(selected ? "SelText" : "Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        grid.Children.Add(label);
        if (count is { } n)
        {
            var badge = new TextBlock { Text = n.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12, Foreground = ContactsUi.Palette("Faint"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badge, 1);
            grid.Children.Add(badge);
        }

        if (menu is not null && moreIcon is not null)
        {
            var more = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 24, Height = 24, CornerRadius = new CornerRadius(8), Content = new SIcon { Kind = "More", Size = 14, StrokeWidth = 2 }, Opacity = 0.7 };
            AutomationProperties.SetAutomationId(more, automationId + ".more");
            AutomationProperties.SetName(more, name + " options");
            more.Click += (_, _) =>
            {
                var p = more.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, more.ActualHeight));
                menu(p.X, p.Y);
            };
            Grid.SetColumn(more, 2);
            grid.Children.Add(more);
        }

        AutomationProperties.SetAutomationId(grid, automationId);
        AutomationProperties.SetName(grid, name);
        grid.Tapped += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                click();
            }
        };
        if (menu is not null)
        {
            grid.RightTapped += (_, e) =>
            {
                var p = e.GetPosition(null);
                menu(p.X, p.Y);
                e.Handled = true;
            };
        }

        return grid;
    }

    private static bool IsInsideButton(DependencyObject? node)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
            {
                return true;
            }
        }

        return false;
    }
}
