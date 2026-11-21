// Mirrors: Rust app.slint:4749-4870 (the contacts sidebar), spec/layout/contacts.md (Bento: tiles; Nordic: dot rows on the ground), Bulwark components/contacts/contacts-sidebar.tsx (:283-317 the h-14 header with the title and the + New menu: contact, group, address book, import; :322-370 flat full-width rows min-h-8 px-3 text-sm with a 16 px icon and a muted count, the selected row bg-accent font-medium;
// :481-560 section headings text-xs font-medium muted, groups and categories); design BNContacts; I-345..I-351, I-066 (the account scope is the active login: one login at a time, as the calendar). Rows open the places of ContactsViewModel; the right click and the "..." button open MENU-BOOK, MENU-GROUP and MENU-CATEGORY (con-ui's providers), the + button MENU-CONTACTS-NEW (CT-01).

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
    private static readonly SolidColorBrush Clear = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    private readonly ContactsViewModel _vm = ContactsUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly StackPanel _stack = new() { Spacing = 12 };
    private readonly Button _new;
    private readonly Border _pane = new();
    private readonly Border _head = new();
    private readonly Button _plus;
    private readonly TextBlock _headTitle = new() { Text = "Contacts", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
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

        // contacts-sidebar.tsx:292-304: the + button beside the title opens the New menu
        _plus = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 32, Height = 32, Content = new SIcon { Kind = "Plus", Size = 16, StrokeWidth = 2 }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(_plus, "contacts.new-menu");
        AutomationProperties.SetName(_plus, "New");
        _plus.Click += (_, _) =>
        {
            var at = _plus.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, _plus.ActualHeight + 4));
            ContactsUi.ShowMenu(ContactsMenuIds.New, at.X, at.Y, new ContactMenuTarget(_vm.AccountKey ?? string.Empty, []));
        };
        var headRow = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        headRow.Children.Add(_headTitle);
        Grid.SetColumn(_plus, 1);
        headRow.Children.Add(_plus);
        _head.Child = headRow;
        AutomationProperties.SetAutomationId(_head, "contacts.sidebar.head");

        var body = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stack };
        var column = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } } };
        column.Children.Add(_head);
        Grid.SetRow(body, 1);
        column.Children.Add(body);
        _pane.Child = column;
        Content = _pane;
        AutomationProperties.SetAutomationId(this, "contacts.sidebar");
        AutomationProperties.SetName(this, "Contacts");
        _vm.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Render);
        _look.PropertyChanged += (_, _) => Render();
        Render();
    }

    private bool Bulwark => _look.IsBulwark;

    private void ApplyLook()
    {
        var bento = _look.IsBento && !Bulwark;
        _new.Height = bento ? 52 : 46;
        _new.CornerRadius = new CornerRadius(bento ? 20 : 16);
        _new.FontSize = 14.5;
        // sidebar.tsx / contacts-sidebar.tsx:283: bg-secondary with a hairline at the end; the header is h-14 with a hairline under it
        _pane.Background = Bulwark ? _look.SidebarBrush : null;
        _pane.BorderBrush = ContactsUi.Palette("Line");
        _pane.BorderThickness = new Thickness(0, 0, Bulwark ? 1 : 0, 0);
        _head.Visibility = Bulwark ? Visibility.Visible : Visibility.Collapsed;
        _head.Height = 56;
        _head.Padding = new Thickness(12, 0, 12, 0);
        _head.BorderBrush = ContactsUi.Palette("Line");
        _head.BorderThickness = new Thickness(0, 0, 0, 1);
        _headTitle.Foreground = ContactsUi.Palette("Text");
        _plus.CornerRadius = _look.ButtonRadius;
        _stack.Spacing = Bulwark ? 0 : 12;
        _stack.Padding = Bulwark ? new Thickness(0, 4, 0, 12) : new Thickness(0, 0, 0, 12);
        foreach (var tile in _tiles)
        {
            tile.Background = bento ? _look.TileBrush : null;
            tile.BorderBrush = bento ? ContactsUi.Palette("Line") : null;
            tile.BorderThickness = new Thickness(bento ? 1 : 0);
            tile.CornerRadius = bento ? _look.PaneRadius : new CornerRadius(0);
            tile.Padding = Bulwark ? new Thickness(0) : bento ? new Thickness(12) : new Thickness(4, 0, 4, 0);
        }
    }

    /// <summary>Redraws the sections from the model.</summary>
    public void Render()
    {
        _tiles.Clear();
        _stack.Children.Clear();
        var bw = Bulwark;
        if (!bw)
        {
            _stack.Children.Add(_new);
        }

        var places = new StackPanel { Spacing = bw ? 0 : 2 };
        places.Children.Add(Row("contacts.place.all", "All contacts", _vm.Contacts.Count, _vm.Place == ContactsPlace.All, () => _vm.ShowPlace(ContactsPlace.All, null), null, null, "Book"));
        if (_vm.DuplicateSetCount > 0)
        {
            places.Children.Add(Row("contacts.place.duplicates", "Duplicates", _vm.DuplicateSetCount, _vm.Place == ContactsPlace.Duplicates, () => _vm.ShowPlace(ContactsPlace.Duplicates, null), null, null, "Users"));
        }

        if (_vm.DirectoryAvailable)
        {
            places.Children.Add(Row("contacts.place.directory", "Directory", null, _vm.Place == ContactsPlace.Directory, () => _vm.ShowPlace(ContactsPlace.Directory, null), null, null, "Folder"));
        }

        AddTile(places);

        var books = new StackPanel { Spacing = bw ? 0 : 2 };
        books.Children.Add(Section("Address books", "contacts.books.add", "New address book", () => ContactsUi.RunCommand(ContactsCommandIds.BookNew)));
        foreach (var b in _vm.Books)
        {
            var id = b.Id!;
            books.Children.Add(Row("contacts.book." + id, b.Name + (b.IsDefault ? " (default)" : string.Empty), b.Count, _vm.Place == ContactsPlace.Book && string.Equals(_vm.PlaceId, id, StringComparison.Ordinal), () => _vm.ShowPlace(ContactsPlace.Book, id), (x, y) => ContactsUi.ShowMenu(ContactsMenuIds.Book, x, y, new ContactMenuTarget(_vm.AccountKey ?? string.Empty, [], BookId: id)), "More", "Book"));
        }

        AddTile(books);

        var groups = new StackPanel { Spacing = bw ? 0 : 2 };
        groups.Children.Add(Section("Groups", "contacts.groups.add", "New group", () => ContactsUi.RunCommand(ContactsCommandIds.NewGroup)));
        foreach (var g in _vm.Groups)
        {
            var id = g.Id!;
            groups.Children.Add(Row("contacts.group." + id, g.Name, g.Count, _vm.Place == ContactsPlace.Group && string.Equals(_vm.PlaceId, id, StringComparison.Ordinal), () => _vm.ShowPlace(ContactsPlace.Group, id), (x, y) => ContactsUi.ShowMenu(ContactsMenuIds.Group, x, y, new ContactMenuTarget(_vm.AccountKey ?? string.Empty, [], GroupId: id)), "More", "Users"));
        }

        if (_vm.Groups.Count == 0)
        {
            groups.Children.Add(Note("No groups yet."));
        }

        AddTile(groups);

        if (_vm.Categories.Count > 0)
        {
            var categories = new StackPanel { Spacing = bw ? 0 : 2 };
            categories.Children.Add(Section("Categories", null, null, null));
            foreach (var c in _vm.Categories)
            {
                var name = c.Name;
                categories.Children.Add(Row("contacts.category." + name, name, c.Count, _vm.Place == ContactsPlace.Category && string.Equals(_vm.PlaceId, name, StringComparison.Ordinal), () => _vm.ShowPlace(ContactsPlace.Category, name), (x, y) => ContactsUi.ShowMenu(ContactsMenuIds.Category, x, y, new ContactMenuTarget(_vm.AccountKey ?? string.Empty, [], Category: name)), "Tag", "Tag"));
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

    private static TextBlock Note(string text) => new() { Text = text, FontSize = 12.5, Foreground = ContactsUi.Palette("Faint"), Margin = new Thickness(StyleState.Current.IsBulwark ? 12 : 2, 2, 0, 2) };

    // :481-560 a section heading is text-xs font-medium muted with a + that sits at the end of the row
    private static Grid Section(string title, string? plusId, string? plusName, Func<bool>? plus)
    {
        var bw = StyleState.Current.IsBulwark;
        var grid = new Grid { Margin = bw ? new Thickness(12, 8, 8, 0) : new Thickness(0, 0, 0, 4), ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        grid.Children.Add(new TextBlock { Text = title, FontSize = bw ? 12 : 11.5, FontWeight = bw ? FontWeights.Medium : FontWeights.Bold, Foreground = ContactsUi.Palette("Dim"), VerticalAlignment = VerticalAlignment.Center });
        if (plusId is not null && plus is not null)
        {
            var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 24, Height = 24, CornerRadius = bw ? StyleState.Current.ButtonRadius : new CornerRadius(8), Content = new SIcon { Kind = "Plus", Size = 14, StrokeWidth = 2 } };
            AutomationProperties.SetAutomationId(button, plusId);
            AutomationProperties.SetName(button, plusName ?? title);
            button.Click += (_, _) => plus();
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
        }

        return grid;
    }

    private static Grid Row(string automationId, string name, int? count, bool selected, Action click, Action<double, double>? menu, string? moreIcon, string rowIcon)
    {
        var bw = StyleState.Current.IsBulwark;
        var grid = new Grid { Height = 32, ColumnSpacing = 8, CornerRadius = bw ? new CornerRadius(0) : new CornerRadius(10), Padding = bw ? new Thickness(12, 0, 8, 0) : new Thickness(8, 0, 4, 0), Background = Fill(selected, bw) };
        var leading = bw ? 1 : 0;
        if (bw)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new SIcon { Kind = rowIcon, Size = 16, StrokeWidth = 1.8, Foreground = ContactsUi.Palette(selected ? "FeedbackSelectedFg" : "Body"), VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(icon);
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = name,
            FontSize = bw ? 14 : 13,
            FontWeight = selected ? (bw ? FontWeights.Medium : FontWeights.SemiBold) : FontWeights.Normal,
            Foreground = ContactsUi.Palette(selected ? (bw ? "FeedbackSelectedFg" : "SelText") : (bw ? "Text" : "Body")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, leading);
        grid.Children.Add(label);
        if (count is { } n)
        {
            var badge = new TextBlock { Text = n.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12, Foreground = ContactsUi.Palette(bw ? "Dim" : "Faint"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badge, leading + 1);
            grid.Children.Add(badge);
        }

        if (menu is not null && moreIcon is not null)
        {
            var more = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 24, Height = 24, CornerRadius = bw ? StyleState.Current.ButtonRadius : new CornerRadius(8), Content = new SIcon { Kind = "More", Size = 14, StrokeWidth = 2 }, Opacity = 0.7 };
            AutomationProperties.SetAutomationId(more, automationId + ".more");
            AutomationProperties.SetName(more, name + " options");
            more.Click += (_, _) =>
            {
                var p = more.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, more.ActualHeight));
                menu(p.X, p.Y);
            };
            Grid.SetColumn(more, leading + 2);
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
        if (bw && !selected)
        {
            // contacts-sidebar.tsx:331: hover:bg-muted. The row draws its own state, so it reads the shared hover brush (no template here)
            grid.PointerEntered += (_, _) => grid.Background = ContactsUi.Palette("FeedbackHover");
            grid.PointerExited += (_, _) => grid.Background = Clear;
        }

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

    private static SolidColorBrush Fill(bool selected, bool bulwark) => selected ? ContactsUi.Palette(bulwark ? "FeedbackSelected" : "Sel") : Clear;

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
