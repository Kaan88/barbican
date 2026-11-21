// Mirrors: Rust app.slint contacts list, shortcuts.rs:204 (the list keys), Bulwark components/contacts/contact-list.tsx (:408-440 toolbar h-14, select toggle, search with the filter toggle and its count, print; :440-520 the filter panel; :522-600 the bulk bar; :600-700 empty states),
// contact-list-item.tsx (click selects and opens, Ctrl-click toggles, select mode, right click menu), ui/toolbar.tsx (32 px controls, TOOLBAR_ACTIVE tint), ui/empty-state.tsx; B-CON-004 (live sort), B-CON-030 (letter headers when groupContactsByLetter is on); I-352..I-358.
// Draws ContactsViewModel; every action is a view model or command call. Bulwark gets the toolbar, the filter panel, the bulk bar and the full-width rows; Bento and Nordic keep their title row and rounded rows.

using System.ComponentModel;
using Barbican.App.Controls;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Contacts;
using Microsoft.UI.Text;
using Windows.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Contacts;

/// <summary>The people list.</summary>
public sealed partial class ContactsListView : UserControl
{
    private static readonly string[] Months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private readonly ContactsViewModel _vm = ContactsUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private bool _syncingSearch;
    private bool _filtersOpen;
    private bool _filterPanelBuilt;
    private bool _syncingFilters;
    private TextBox? _org;
    private TextBox? _job;
    private TextBox? _where;
    private TextBox? _domain;
    private Button? _month;
    private Button? _hasEmail;
    private Button? _hasPhone;
    private Button? _hasPhoto;
    private TextBlock? _bulkCount;
    private Button? _bulkAll;
    private Button? _bulkDelete;
    private Button? _bulkExport;
    private Button? _bulkGroup;

    public ContactsListView()
    {
        InitializeComponent();
        ImportButton.Content = new SIcon { Kind = "Send", Size = 15, StrokeWidth = 2 };
        ExportButton.Content = new SIcon { Kind = "Download", Size = 15, StrokeWidth = 2 };
        SelectButton.Content = new SIcon { Kind = "Check", Size = 16, StrokeWidth = 2 };
        PrintButton.Content = new SIcon { Kind = "Print", Size = 16, StrokeWidth = 2 };
        ClearSearch.Content = new SIcon { Kind = "Close", Size = 14, StrokeWidth = 2.2 };
        _vm.PropertyChanged += OnModelChanged;
        _look.PropertyChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
        Render();
    }

    /// <summary>The search field (the screen focuses it for "/").</summary>
    public TextBox SearchBox => Search;

    /// <summary>The list control (automation and the screen read it).</summary>
    public ListView Rows => List;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            Render();
        }
        else
        {
            _ = DispatcherQueue.TryEnqueue(Render);
        }
    }

    // contact-list.tsx:359 hasSelection: the boxes and the bulk bar show while rows are ticked (select mode, or Ctrl/Shift on several); the one open contact is not a tick
    private bool Ticking => _vm.SelectMode || _vm.SelectedIds.Count > 1;

    // a selection made without select mode has no mode to leave: turning it on and off empties it
    private void ClearTicks()
    {
        _vm.SelectMode = true;
        _vm.SelectMode = false;
    }

    // places the parts of the head for the style on show (contact-list.tsx:408-440 for Bulwark)
    private void ApplyLook()
    {
        var bw = _look.IsBulwark;
        HeadBox.BorderThickness = new Thickness(0, 0, 0, bw ? 1 : 0);
        HeadBox.Padding = bw ? new Thickness(12, 0, 12, 0) : new Thickness(0);
        HeadBox.Height = bw ? 56 : double.NaN;
        Head.RowSpacing = bw ? 0 : 8;
        Head.VerticalAlignment = bw ? VerticalAlignment.Center : VerticalAlignment.Top;
        LegacyTitle.Visibility = ImportButton.Visibility = ExportButton.Visibility = bw ? Visibility.Collapsed : Visibility.Visible;
        PrintButton.Visibility = bw ? Visibility.Visible : Visibility.Collapsed;
        PrintButton.CornerRadius = SelectButton.CornerRadius = _look.ButtonRadius;
        SelectButton.CornerRadius = bw ? _look.ButtonRadius : new CornerRadius(10);
        SelectButton.Width = SelectButton.Height = bw ? 32 : 30;
        Grid.SetRow(SelectButton, 0);
        Grid.SetColumn(SelectButton, bw ? 0 : 4);
        Grid.SetRow(SearchField, bw ? 0 : 1);
        Grid.SetColumn(SearchField, bw ? 1 : 0);
        Grid.SetColumnSpan(SearchField, bw ? 1 : 5);
        if (bw)
        {
            // toolbar.tsx:90-96: h-8, text starts after the glyph (ps-8) and clears the two end buttons (pe-16)
            SearchField.Height = 32;
            SearchField.Background = null;
            SearchField.CornerRadius = new CornerRadius(0);
            SearchField.Padding = new Thickness(0);
            Search.MinHeight = 32;
            Search.Height = 32;
            Search.Padding = new Thickness(32, 6, 56, 6);
            Search.VerticalContentAlignment = VerticalAlignment.Center;
            Search.BorderThickness = new Thickness(1);
            Search.ClearValue(Control.BackgroundProperty);
            SearchGlyph.Visibility = Visibility.Visible;
            ClearSearch.CornerRadius = FilterToggle.CornerRadius = new CornerRadius(6);
        }
        else
        {
            SearchField.Height = 38;
            SearchField.CornerRadius = new CornerRadius(14);
            SearchField.Background = AppResources.Brush("Hover");
            SearchField.Padding = new Thickness(0);
            Search.MinHeight = 0;
            Search.Height = double.NaN;
            Search.Padding = new Thickness(12, 0, 56, 0);
            Search.BorderThickness = new Thickness(0);
            Search.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            SearchGlyph.Visibility = Visibility.Collapsed;
            ClearSearch.CornerRadius = FilterToggle.CornerRadius = new CornerRadius(8);
        }

        List.ItemContainerStyle = bw ? (Style)Application.Current.Resources["ListItem.Row"] : (Style)Resources["ContactsLegacyRow"];
        List.Padding = bw ? new Thickness(0) : new Thickness(0, 2, 0, 8);
        FilterPanel.Background = bw ? Dimmed("Track", 0.3) : null;
        BulkBarBulwark.Background = bw ? Dimmed("FeedbackSelected", 0.3) : null;
    }

    private static SolidColorBrush Dimmed(string key, double opacity) => new(AppResources.Brush(key).Color) { Opacity = opacity };

    /// <summary>Redraws the header, the notice, the bulk bar and the rows from the model in one pass.</summary>
    public void Render()
    {
        ApplyLook();
        var bw = _look.IsBulwark;
        var place = PlaceTitle();
        Title.Text = place;
        Count.Text = _vm.Rows.Count == 1 ? "1 contact" : _vm.Rows.Count + " contacts";
        if (!string.Equals(Search.Text, _vm.SearchText, StringComparison.Ordinal))
        {
            _syncingSearch = true;
            Search.Text = _vm.SearchText;
            _syncingSearch = false;
        }

        ClearSearch.Visibility = _vm.SearchText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.IsEnabled = _vm.Rows.Count > 0;
        ImportButton.IsEnabled = _vm.AccountKey is not null;
        PrintButton.IsEnabled = _vm.Rows.Count > 0;
        SelectButton.IsChecked = Ticking;
        var active = _vm.Filters.ActiveCount;
        FilterToggle.IsChecked = _filtersOpen || active > 0;
        FilterBadge.Visibility = !_filtersOpen && active > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterBadgeText.Text = active.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FilterPanel.Visibility = _filtersOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_filtersOpen)
        {
            RefreshFilterPanel();
        }

        var error = _vm.LoadError;
        Notice.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        NoticeText.Text = error is null ? string.Empty : "Couldn't load contacts: " + error;
        BulkBar.Visibility = Ticking && !bw ? Visibility.Visible : Visibility.Collapsed;
        BulkCount.Text = _vm.SelectedIds.Count + " selected";
        BulkDelete.IsEnabled = _vm.SelectedIds.Count > 0;
        BulkBarBulwark.Visibility = Ticking && bw ? Visibility.Visible : Visibility.Collapsed;
        if (Ticking && bw)
        {
            RefreshBulwarkBulk();
        }

        var bento = _look.IsBento;
        var items = new List<ContactsListItem>(_vm.Rows.Count + 27);
        string? letter = null;
        foreach (var row in _vm.Rows)
        {
            if (_vm.GroupByLetter && !string.Equals(row.Letter, letter, StringComparison.Ordinal))
            {
                letter = row.Letter;
                items.Add(new ContactsListItem(letter));
            }

            items.Add(new ContactsListItem(row, _vm.SelectedIds.Contains(row.Id), Ticking, bento));
        }

        List.ItemsSource = items;
        var showEmpty = items.Count == 0 && !_vm.IsLoading;
        EmptyPanel.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (showEmpty)
        {
            BuildEmpty();
        }

        ScrollToSelection();
    }

    private string PlaceTitle() => _vm.Place switch
    {
        ContactsPlace.Book => _vm.Books.FirstOrDefault(b => string.Equals(b.Id, _vm.PlaceId, StringComparison.Ordinal))?.Name ?? "Address book",
        ContactsPlace.Group => _vm.Groups.FirstOrDefault(g => string.Equals(g.Id, _vm.PlaceId, StringComparison.Ordinal))?.Name ?? "Group",
        ContactsPlace.Category => _vm.PlaceId ?? "Category",
        ContactsPlace.Directory => "Directory",
        ContactsPlace.Duplicates => "Duplicates",
        _ => "Contacts",
    };

    // contact-list.tsx:600-690: a search or filter with no match says so and offers to clear it; an empty account or place offers New and Import (ui/empty-state.tsx compact)
    private void BuildEmpty()
    {
        EmptyActions.Children.Clear();
        var searching = _vm.SearchText.Length > 0;
        var filtering = _vm.Filters.ActiveCount > 0;
        if (searching || filtering)
        {
            EmptyIcon.Kind = filtering && !searching ? "Filter" : "Search";
            Empty.Text = searching ? "No contacts match your search" : "No contacts match your filters";
            EmptyHint.Text = searching ? "Try a different search term" : "Try adjusting or clearing filters";
            if (searching)
            {
                EmptyActions.Children.Add(OutlineButton("contacts.empty.clear-search", "Clear search", null, () => _vm.SearchText = string.Empty));
            }

            if (filtering)
            {
                EmptyActions.Children.Add(OutlineButton("contacts.empty.clear-filters", "Clear", "Undo", () => _vm.Filters = ContactsFilters.None));
            }

            return;
        }

        EmptyIcon.Kind = _vm.Place switch { ContactsPlace.Directory => "Users", ContactsPlace.Duplicates => "Users", _ => "Book" };
        switch (_vm.Place)
        {
            case ContactsPlace.Duplicates:
                Empty.Text = "No duplicate contacts found";
                EmptyHint.Text = string.Empty;
                break;
            case ContactsPlace.Directory:
                Empty.Text = "The directory is empty";
                EmptyHint.Text = string.Empty;
                break;
            case ContactsPlace.All:
                Empty.Text = _vm.AccountKey is null ? "Sign in to see your contacts" : "No contacts yet";
                EmptyHint.Text = _vm.AccountKey is null ? string.Empty : "Create your first contact or import from a vCard file";
                break;
            default:
                Empty.Text = "No contacts here";
                EmptyHint.Text = "Add a contact or import a vCard file.";
                break;
        }

        EmptyHint.Visibility = EmptyHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_vm.AccountKey is not null && _vm.Place is not (ContactsPlace.Duplicates or ContactsPlace.Directory))
        {
            var create = new Button { Style = (Style)Application.Current.Resources["Button.Primary"], Height = 32, CornerRadius = _look.ButtonRadius, FontSize = 14, FontWeight = FontWeights.Medium, Padding = new Thickness(12, 0, 12, 0), Content = IconLabel("Plus", "New contact", 16) };
            AutomationProperties.SetAutomationId(create, "contacts.empty.new");
            create.Click += (_, _) => _ = ContactsUi.RunCommand(ContactsCommandIds.New, _vm.Place == ContactsPlace.Book ? _vm.PlaceId : null);
            EmptyActions.Children.Add(create);
            EmptyActions.Children.Add(OutlineButton("contacts.empty.import", "Import contacts", "Send", () => _ = ContactsUi.RunCommand(ContactsCommandIds.Import)));
        }
    }

    private Button OutlineButton(string id, string label, string? icon, Action click)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Height = 32, CornerRadius = _look.ButtonRadius, FontSize = 14, FontWeight = FontWeights.Medium, Padding = new Thickness(12, 0, 12, 0), Content = IconLabel(icon, label, 16) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    private static StackPanel IconLabel(string? icon, string label, double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon is not null)
        {
            row.Children.Add(new SIcon { Kind = icon, Size = size, StrokeWidth = 2 });
        }

        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    // ---- the filter panel (contact-list.tsx:440-520), built the first time it opens ----

    private void OnFilterToggle(object sender, RoutedEventArgs e)
    {
        _filtersOpen = !_filtersOpen;
        if (_filtersOpen && !_filterPanelBuilt)
        {
            BuildFilterPanel();
        }

        Render();
    }

    private void BuildFilterPanel()
    {
        _filterPanelBuilt = true;
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } }, ColumnSpacing = 4 };
        head.Children.Add(new TextBlock { Text = "Advanced filters", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = ContactsUi.Palette("Text"), VerticalAlignment = VerticalAlignment.Center });
        var clear = GhostButton("contacts.filters.clear", "Clear", "Undo", () => _vm.Filters = ContactsFilters.None);
        Grid.SetColumn(clear, 1);
        head.Children.Add(clear);
        var close = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 28, Height = 28, CornerRadius = _look.ButtonRadius, Content = new SIcon { Kind = "Close", Size = 14, StrokeWidth = 2.2 } };
        AutomationProperties.SetAutomationId(close, "contacts.filters.close");
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) =>
        {
            _filtersOpen = false;
            Render();
        };
        Grid.SetColumn(close, 2);
        head.Children.Add(close);
        FilterBody.Children.Add(head);

        _org = Field("Company", "e.g. Acme Corp", "organization", v => _vm.Filters = _vm.Filters with { Organization = v });
        _job = Field("Job title", "e.g. Designer", "job", v => _vm.Filters = _vm.Filters with { JobTitle = v });
        _where = Field("Location", "City or country", "location", v => _vm.Filters = _vm.Filters with { Location = v });
        _domain = Field("Email domain", "example.com", "domain", v => _vm.Filters = _vm.Filters with { EmailDomain = v });
        FilterBody.Children.Add(TwoUp(_org, _job));
        FilterBody.Children.Add(TwoUp(_where, _domain));

        var monthLabel = new TextBlock { Text = "Birthday in", FontSize = 12, Foreground = ContactsUi.Palette("Dim"), Margin = new Thickness(0, 0, 0, 4) };
        // a drop-down opens a popup (ADR 0007): the month button steps through Any month, January, February, ...
        _month = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 14, FontWeight = FontWeights.Normal, CornerRadius = _look.FieldRadius, Padding = new Thickness(10, 0, 10, 0) };
        AutomationProperties.SetAutomationId(_month, "contacts.filters.month");
        _month.Click += (_, _) =>
        {
            var next = _vm.Filters.BirthdayMonth is { } m ? (m >= 12 ? (int?)null : m + 1) : 1;
            _vm.Filters = _vm.Filters with { BirthdayMonth = next };
        };
        var monthBox = new StackPanel();
        monthBox.Children.Add(monthLabel);
        monthBox.Children.Add(_month);
        FilterBody.Children.Add(monthBox);

        _hasEmail = Chip("contacts.filters.has-email", "Mail", "Has email", () => _vm.Filters = _vm.Filters with { HasEmail = ContactsFilters.Cycle(_vm.Filters.HasEmail) });
        _hasPhone = Chip("contacts.filters.has-phone", "Phone", "Has phone", () => _vm.Filters = _vm.Filters with { HasPhone = ContactsFilters.Cycle(_vm.Filters.HasPhone) });
        _hasPhoto = Chip("contacts.filters.has-photo", "Image", "Has photo", () => _vm.Filters = _vm.Filters with { HasPhoto = ContactsFilters.Cycle(_vm.Filters.HasPhoto) });
        var chips = new WrapRow { Spacing = 8 };
        chips.Children.Add(_hasEmail);
        chips.Children.Add(_hasPhone);
        chips.Children.Add(_hasPhoto);
        FilterBody.Children.Add(chips);
    }

    private TextBox Field(string label, string placeholder, string id, Action<string> set)
    {
        var box = new TextBox { PlaceholderText = placeholder, MinHeight = 32, Height = 32, FontSize = 14, Padding = new Thickness(10, 6, 10, 6), VerticalContentAlignment = VerticalAlignment.Center, Tag = label };
        AutomationProperties.SetAutomationId(box, "contacts.filters." + id);
        AutomationProperties.SetName(box, label);
        box.TextChanged += (_, _) =>
        {
            if (!_syncingFilters)
            {
                set(box.Text);
            }
        };
        return box;
    }

    private static Grid TwoUp(TextBox a, TextBox b)
    {
        var grid = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
        grid.Children.Add(Labelled(a));
        var right = Labelled(b);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static StackPanel Labelled(TextBox box)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = (string)box.Tag, FontSize = 12, Foreground = ContactsUi.Palette("Dim") });
        panel.Children.Add(box);
        return panel;
    }

    // contact-list.tsx:64-80 ToggleChip: on = primary tint with a primary border, off = muted with a line through, any = plain
    private static Button Chip(string id, string icon, string label, Action click)
    {
        var chip = new Button { Style = (Style)Application.Current.Resources["Button.Chip"], Content = IconLabel(icon, label, 14), Tag = label };
        AutomationProperties.SetAutomationId(chip, id);
        chip.Click += (_, _) => click();
        return chip;
    }

    private static void StyleChip(Button chip, bool? value)
    {
        if (chip.Content is StackPanel { Children.Count: 2 } row && row.Children[1] is TextBlock text)
        {
            text.TextDecorations = value == false ? TextDecorations.Strikethrough : TextDecorations.None;
        }

        chip.Foreground = value switch { true => AppResources.Brush("FeedbackActiveFg"), false => AppResources.Brush("Dim"), _ => AppResources.Brush("Dim") };
        chip.Background = value switch { true => AppResources.Brush("FeedbackActive"), false => AppResources.Brush("Track"), _ => new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        chip.BorderBrush = value == true ? AppResources.Brush("FeedbackActiveFg") : AppResources.Brush("Line");
    }

    private void RefreshFilterPanel()
    {
        if (!_filterPanelBuilt)
        {
            BuildFilterPanel();
        }

        var f = _vm.Filters;
        _syncingFilters = true;
        SetText(_org, f.Organization);
        SetText(_job, f.JobTitle);
        SetText(_where, f.Location);
        SetText(_domain, f.EmailDomain);
        _syncingFilters = false;
        if (_month is not null)
        {
            _month.Content = f.BirthdayMonth is { } m && m is >= 1 and <= 12 ? Months[m - 1] : "Any month";
        }

        StyleChip(_hasEmail!, f.HasEmail);
        StyleChip(_hasPhone!, f.HasPhone);
        StyleChip(_hasPhoto!, f.HasPhoto);
    }

    private static void SetText(TextBox? box, string value)
    {
        if (box is not null && !string.Equals(box.Text, value, StringComparison.Ordinal))
        {
            box.Text = value;
        }
    }

    // ---- the Bulwark bulk bar (contact-list.tsx:522-600), built the first time it shows ----

    private Button GhostButton(string id, string label, string icon, Action click)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Height = 28, CornerRadius = _look.ButtonRadius, Padding = new Thickness(8, 0, 8, 0), FontSize = 12, FontWeight = FontWeights.Medium, Content = IconLabel(icon, label, 14) };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }

    private void RefreshBulwarkBulk()
    {
        if (_bulkCount is null)
        {
            var bar = new Grid { ColumnSpacing = 4, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
            _bulkAll = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 24, Height = 24, CornerRadius = _look.ButtonRadius };
            AutomationProperties.SetAutomationId(_bulkAll, "contacts.bulk.all");
            _bulkAll.Click += (_, _) =>
            {
                if (_vm.Rows.Count > 0 && _vm.SelectedIds.Count == _vm.Rows.Count)
                {
                    ClearTicks();
                }
                else
                {
                    _vm.SelectAll();
                }
            };
            bar.Children.Add(_bulkAll);
            _bulkCount = new TextBlock { FontSize = 12, FontWeight = FontWeights.Medium, Foreground = ContactsUi.Palette("Text"), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(_bulkCount, "contacts.bulk.count");
            Grid.SetColumn(_bulkCount, 1);
            bar.Children.Add(_bulkCount);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
            _bulkGroup = GhostButton("contacts.bulk.group", "Add to group", "Users", OnBulkGroup);
            _bulkExport = GhostButton("contacts.bulk.export", "Export", "Download", () => OnExport(this, new RoutedEventArgs()));
            _bulkDelete = GhostButton("contacts.bulk.delete", "Delete", "Trash", () => OnBulkDelete(this, new RoutedEventArgs()));
            _bulkDelete.Foreground = ContactsUi.Palette("RoseFg");
            var clear = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 28, Height = 28, CornerRadius = _look.ButtonRadius, Content = new SIcon { Kind = "Close", Size = 14, StrokeWidth = 2.2 } };
            AutomationProperties.SetAutomationId(clear, "contacts.bulk.done");
            AutomationProperties.SetName(clear, "Clear selection");
            clear.Click += (_, _) => ClearTicks();
            actions.Children.Add(_bulkGroup);
            actions.Children.Add(_bulkExport);
            actions.Children.Add(_bulkDelete);
            actions.Children.Add(clear);
            Grid.SetColumn(actions, 2);
            bar.Children.Add(actions);
            BulkBarBulwark.Child = bar;
        }

        var n = _vm.SelectedIds.Count;
        var all = _vm.Rows.Count > 0 && n == _vm.Rows.Count;
        _bulkCount.Text = n == 1 ? "1 selected" : n + " selected";
        _bulkAll!.Content = new Grid { Width = 16, Height = 16, Children = { new Border { BorderBrush = ContactsUi.Palette(all ? "Accent" : "Dim"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3) }, new TextBlock { Text = "", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = ContactsUi.Palette("Accent"), Visibility = all ? Visibility.Visible : Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
        AutomationProperties.SetName(_bulkAll, all ? "Clear selection" : "Select all");
        _bulkGroup!.IsEnabled = _bulkExport!.IsEnabled = _bulkDelete!.IsEnabled = n > 0;
    }

    private void OnBulkGroup()
    {
        if (_vm.AccountKey is { } key && _bulkGroup is not null)
        {
            var at = _bulkGroup.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, _bulkGroup.ActualHeight));
            ContactsUi.ShowMenu(ContactsMenuIds.ContactGroups, at.X, at.Y, new ContactMenuTarget(key, [.. _vm.SelectedIds]));
        }
    }

    private void ScrollToSelection()
    {
        if (List.ItemsSource is List<ContactsListItem> items && _vm.OpenId is { } id && items.Find(i => i.Row is not null && string.Equals(i.Row.Id, id, StringComparison.Ordinal)) is { } item)
        {
            List.ScrollIntoView(item);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncingSearch)
        {
            _vm.SearchText = Search.Text;
        }
    }

    // a TextBox keeps its keys from the shell's router: Escape clears the search, Down moves into the list
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            _vm.Clear();
        }
        else if (e.Key == Windows.System.VirtualKey.Down)
        {
            e.Handled = true;
            _vm.Move(1);
            _ = List.Focus(FocusState.Programmatic);
        }
    }

    private void OnClearSearch(object sender, RoutedEventArgs e) => _vm.SearchText = string.Empty;

    private void OnRetry(object sender, RoutedEventArgs e) => ContactsUi.Run(() => _vm.RefreshAsync(CancellationToken.None));

    private void OnImport(object sender, RoutedEventArgs e) => _ = ContactsUi.RunCommand(ContactsCommandIds.Import);

    private void OnExport(object sender, RoutedEventArgs e) => _ = ContactsUi.RunCommand(ContactsCommandIds.Export, _vm.SelectedIds.Count > 0 ? ContactsArg.Join([.. _vm.SelectedIds]) : null);

    // toolbar.tsx / contact-list.tsx:25-28: print the list as it is on screen (filtered, sorted, grouped)
    private void OnPrint(object sender, RoutedEventArgs e) => _ = ContactsUi.RunCommand(ContactsCommandIds.Print);

    private void OnSelectMode(object sender, RoutedEventArgs e)
    {
        if (Ticking)
        {
            ClearTicks();
        }
        else
        {
            _vm.SelectMode = true;
        }
    }

    private void OnSelectAll(object sender, RoutedEventArgs e) => _vm.SelectAll();

    private void OnBulkDelete(object sender, RoutedEventArgs e) => _ = ContactsUi.RunCommand(ContactsCommandIds.Delete, ContactsArg.Join([.. _vm.SelectedIds]));

    // D-M5-B01: a click selects and opens; in select mode (and with Ctrl) it toggles without opening
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ContactsListItem { Row: { } row })
        {
            return;
        }

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (Ticking || ctrl)
        {
            _vm.SelectContact(row.Id, true);
            return;
        }

        _vm.SelectContact(row.Id, false);
        _vm.Open(row.Id);
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Item(e.OriginalSource) is { Row: { } row })
        {
            _ = ContactsUi.RunCommand(ContactsCommandIds.Edit, row.Id);
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (Item(e.OriginalSource) is { Row: { } row })
        {
            var p = e.GetPosition(null);
            ContactsUi.ShowContactMenu(row.Id, p.X, p.Y);
            e.Handled = true;
        }
    }

    /// <summary>Opens MENU-CONTACT on the selected row (Shift+F10).</summary>
    public void ShowSelectionMenu()
    {
        var id = _vm.OpenId ?? _vm.SelectedIds.FirstOrDefault();
        if (id is null)
        {
            return;
        }

        var at = List.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(60, 60));
        ContactsUi.ShowContactMenu(id, at.X, at.Y);
    }

    private static ContactsListItem? Item(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { DataContext: ContactsListItem item })
            {
                return item;
            }
        }

        return null;
    }
}
