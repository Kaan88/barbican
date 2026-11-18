// Mirrors: Rust app.slint contacts list, shortcuts.rs:204 (the list keys), Bulwark components/contacts/contact-list.tsx, contact-list-item.tsx (click selects and opens, Ctrl-click toggles, select mode with a bulk bar, right click menu, the load error with Retry, empty states);
// B-CON-004 (live sort), B-CON-030 (letter headers when groupContactsByLetter is on); I-352..I-358. Draws ContactsViewModel; every action is a view model or command call.

using System.ComponentModel;
using Barbican.App.Controls;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Contacts;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Contacts;

/// <summary>The people list.</summary>
public sealed partial class ContactsListView : UserControl
{
    private readonly ContactsViewModel _vm = ContactsUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private bool _syncingSearch;

    public ContactsListView()
    {
        InitializeComponent();
        ImportButton.Content = new SIcon { Kind = "Send", Size = 15, StrokeWidth = 2 };
        ExportButton.Content = new SIcon { Kind = "Download", Size = 15, StrokeWidth = 2 };
        SelectButton.Content = new SIcon { Kind = "Check", Size = 15, StrokeWidth = 2 };
        ClearSearch.Content = new SIcon { Kind = "Close", Size = 12, StrokeWidth = 2.4 };
        SearchGlyph.Text = string.Empty;
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

    /// <summary>Redraws the header, the notice, the bulk bar and the rows from the model in one pass.</summary>
    public void Render()
    {
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
        SelectButton.Background = _vm.SelectMode ? AppResources.Brush("Sel") : null;
        var error = _vm.LoadError;
        Notice.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        NoticeText.Text = error is null ? string.Empty : "Couldn't load contacts: " + error;
        BulkBar.Visibility = _vm.SelectMode ? Visibility.Visible : Visibility.Collapsed;
        BulkCount.Text = _vm.SelectedIds.Count + " selected";
        BulkDelete.IsEnabled = _vm.SelectedIds.Count > 0;
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

            items.Add(new ContactsListItem(row, _vm.SelectedIds.Contains(row.Id), _vm.SelectMode, bento));
        }

        List.ItemsSource = items;
        Empty.Text = EmptyText();
        Empty.Visibility = items.Count == 0 && !_vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
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

    private string EmptyText()
    {
        if (_vm.SearchText.Length > 0)
        {
            return "No contacts match \"" + _vm.SearchText + "\".";
        }

        return _vm.Place switch
        {
            ContactsPlace.Group => "No one is in this group yet.",
            ContactsPlace.Category => "No contacts have this category.",
            ContactsPlace.Duplicates => "No duplicate contacts found.",
            ContactsPlace.Directory => "The directory is empty.",
            _ => _vm.AccountKey is null ? "Sign in to see your contacts." : "No contacts yet. Add one, or import a .vcf file.",
        };
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

    private void OnSelectMode(object sender, RoutedEventArgs e) => _vm.SelectMode = !_vm.SelectMode;

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
        if (_vm.SelectMode || ctrl)
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
