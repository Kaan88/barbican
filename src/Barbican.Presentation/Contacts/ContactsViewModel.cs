// Mirrors: Rust contacts.rs (ST :21, load, select, filter, sort_name :83), Bulwark components/contacts/contacts-app.tsx, contact-list.tsx (letter groups, search, sort), contacts-sidebar.tsx (books, groups, categories, duplicates, directory);
// B-CON-004 (sort by last name, live), B-CON-027, B-CON-030, I-345..I-358; hooks onContactOpen, onContactSelectionChange (spec/plugins/hooks.csv, owner con-ui).
// Decisions: D-M5-B01 a plain click selects and opens, Ctrl-click toggles without opening; D-M5-B02 the arrow keys select and open the row they land on.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Contacts;

/// <summary>The default <see cref="IContactsViewModel"/>. Every change of state raises <c>PropertyChanged("")</c> once, on the UI thread, after the new state is complete.</summary>
public sealed class ContactsViewModel : ObservableObject, IContactsViewModel, IDisposable
{
    private readonly IContactStore _store;
    private readonly IContactWriter _writer;
    private readonly IPrefsStore _prefs;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;
    private readonly IContactsAccountSource _source;
    private readonly IDispatcher _dispatcher;
    private readonly IContactDirectory? _directory;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private ContactsPlace _place = ContactsPlace.All;
    private string? _placeId;
    private string _search = string.Empty;
    private ContactsFilters _filters = ContactsFilters.None;
    private bool _byLast;
    private bool _byLetter = true;
    private bool _showDirectory = true;
    private bool _loading;
    private string? _error;
    private bool _selectMode;
    private string? _openId;
    private string? _anchorId;
    private IReadOnlyList<JsonNode> _contacts = [];
    private IReadOnlyList<JsonNode> _groups = [];
    private IReadOnlyList<JsonNode> _books = [];
    private List<ContactsRow> _rows = [];
    private IReadOnlyList<ContactsSidebarEntry> _bookEntries = [];
    private IReadOnlyList<ContactsSidebarEntry> _groupEntries = [];
    private IReadOnlyList<ContactsSidebarEntry> _categoryEntries = [];
    private List<IReadOnlyList<JsonNode>> _duplicates = [];
    private IReadOnlyList<JsonNode> _directoryCards = [];
    private string? _loadedFor;
    private bool _disposed;

    public ContactsViewModel(IContactStore store, IContactWriter writer, IPrefsStore prefs, IHookBus hooks, TimeProvider time, IContactsAccountSource source, IDispatcher dispatcher, IContactDirectory? directory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _store = store;
        _writer = writer;
        _prefs = prefs;
        _hooks = hooks;
        _time = time;
        _source = source;
        _dispatcher = dispatcher;
        _directory = directory;
        ReadPrefs();
        _store.Changed += OnStoreChanged;
        _prefs.Changed += OnPrefsChanged;
        _source.Changed += OnSourceChanged;
        Rebuild();
    }

    public ContactsPlace Place => _place;

    public string? PlaceId => _placeId;

    public IReadOnlyList<ContactsRow> Rows => _rows;

    public IReadOnlySet<string> SelectedIds => _selected;

    public string SearchText
    {
        get => _search;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(next, _search, StringComparison.Ordinal))
            {
                return;
            }

            _search = next;
            Rebuild();
        }
    }

    /// <summary>Set by the Contacts screen: shows a contact form in its pane and answers when it ends, or returns null when the pane is not on screen (the form then opens as a dialog).</summary>
    public Func<ContactsFormModel, Task<bool>?>? FormHost { get; set; }

    /// <summary>The filters of the list panel (Bulwark contact-list.tsx:34-90); the list and its empty state follow them.</summary>
    public ContactsFilters Filters
    {
        get => _filters;
        set
        {
            var next = value ?? ContactsFilters.None;
            if (next == _filters)
            {
                return;
            }

            _filters = next;
            Rebuild();
        }
    }

    public bool SortByLastName => _byLast;

    public bool GroupByLetter => _byLetter;

    public bool IsLoading => _loading;

    public string? LoadError => _error;

    public string? AccountKey => _source.AccountKey;

    public IReadOnlyList<ContactsSidebarEntry> Books => _bookEntries;

    public IReadOnlyList<ContactsSidebarEntry> Groups => _groupEntries;

    public IReadOnlyList<ContactsSidebarEntry> Categories => _categoryEntries;

    public bool DirectoryAvailable => _showDirectory && _directory is not null && _source.AccountKey is { } key && IsAvailable(_directory, key);

    public int DuplicateSetCount => _duplicates.Count;

    public bool SelectMode
    {
        get => _selectMode;
        set
        {
            if (_selectMode == value)
            {
                return;
            }

            _selectMode = value;
            if (!value)
            {
                SetSelection([]);
            }

            Changed();
        }
    }

    public string? OpenId => _openId;

    public JsonNode? OpenCard => _openId is null ? null : CardOf(_openId);

    public IReadOnlyList<ContactLine> DetailLines => OpenCard is { } card ? ContactsDetailLines.Of(card, ContactDates.Today(_time, _prefs.Current.Str("timeZone", "auto"))) : [];

    public IReadOnlyList<JsonNode> Contacts => _contacts;

    public IReadOnlyList<JsonNode> GroupCards => _groups;

    public IReadOnlyList<IReadOnlyList<JsonNode>> DuplicateSets => _duplicates;

    public JsonNode? CardOf(string id) =>
        _contacts.Concat(_groups).Concat(_directoryCards).FirstOrDefault(c => string.Equals(ContactsJson.Id(c), id, StringComparison.Ordinal));

    public void ShowPlace(ContactsPlace place, string? placeId)
    {
        _place = place;
        _placeId = place is ContactsPlace.Book or ContactsPlace.Group or ContactsPlace.Category ? placeId : null;
        _search = string.Empty;
        SetSelection([]);
        _openId = null;
        _selectMode = false;
        if (place == ContactsPlace.Directory)
        {
            _ = LoadDirectoryAsync();
        }

        Rebuild();
    }

    public void SelectContact(string id, bool extend)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!_rows.Any(r => string.Equals(r.Id, id, StringComparison.Ordinal)))
        {
            return;
        }

        var next = extend ? new HashSet<string>(_selected, StringComparer.Ordinal) : [];
        if (extend && !next.Add(id))
        {
            _ = next.Remove(id);
        }
        else if (!extend)
        {
            _ = next.Add(id);
        }

        _anchorId = id;
        SetSelection(next);
        Changed();
    }

    public void Open(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var card = CardOf(id);
        if (card is null)
        {
            return;
        }

        _openId = id;
        _hooks.Fire(HookNames.OnContactOpen, () => new ContactsOpenPayload(id, ContactsListRules.FullName(card), [.. ContactsJson.Emails(card)], ContactsJson.FirstOf(card["organizations"], "name")));
        Changed();
    }

    public void Move(int delta)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var current = _anchorId is null ? -1 : IndexOf(_anchorId);
        var target = current < 0 ? (delta >= 0 ? 0 : _rows.Count - 1) : Math.Clamp(current + delta, 0, _rows.Count - 1);
        Land(_rows[target].Id);
    }

    public void Jump(bool last)
    {
        if (_rows.Count > 0)
        {
            Land(_rows[last ? ^1 : 0].Id);
        }
    }

    public void Clear()
    {
        if (_search.Length > 0)
        {
            SearchText = string.Empty;
            return;
        }

        _openId = null;
        _anchorId = null;
        SetSelection([]);
        Changed();
    }

    public void SelectAll()
    {
        SetSelection(_rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal));
        Changed();
    }

    public async Task<Result<Unit>> DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (_source.AccountKey is not { } key || ids.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var result = await _writer.DeleteAsync(key, ids, ct).ConfigureAwait(true);
        if (result.IsOk)
        {
            var gone = ids.ToHashSet(StringComparer.Ordinal);
            _selected.ExceptWith(gone);
            if (_openId is { } open && gone.Contains(open))
            {
                _openId = null;
            }

            Rebuild();
        }

        return result;
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (_source.AccountKey is not { } key)
        {
            _loadedFor = null;
            Rebuild();
            return;
        }

        _loading = true;
        _error = null;
        Changed();
        try
        {
            var result = await _store.LoadAsync(key, ct).ConfigureAwait(true);
            _loading = false;
            _error = result.IsOk ? null : result.Error!.Message;
            _loadedFor = result.IsOk ? key : null;
        }
        catch (PortPendingException e)
        {
            _loading = false;
            _error = "Contacts are not available yet (" + e.Message + ")";
        }

        Rebuild();
    }

    public bool IsLoadedFor(string accountKey) => string.Equals(_loadedFor, accountKey, StringComparison.Ordinal);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Changed -= OnStoreChanged;
        _prefs.Changed -= OnPrefsChanged;
        _source.Changed -= OnSourceChanged;
    }

    private static bool IsAvailable(IContactDirectory directory, string key)
    {
        try
        {
            return directory.IsAvailable(key);
        }
        catch (PortPendingException)
        {
            return false;
        }
    }

    private void Land(string id)
    {
        _anchorId = id;
        SetSelection([id]);
        Open(id);
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (string.Equals(_rows[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void SetSelection(IEnumerable<string> ids)
    {
        var next = ids.ToHashSet(StringComparer.Ordinal);
        if (next.SetEquals(_selected))
        {
            return;
        }

        _selected.Clear();
        _selected.UnionWith(next);
        _hooks.Fire(HookNames.OnContactSelectionChange, () => new ContactsSelectionPayload([.. _selected.Order(StringComparer.Ordinal)]));
    }

    private void ReadPrefs()
    {
        var p = _prefs.Current;
        _byLast = p.Bool("sortContactsByLastName", false);
        _byLetter = p.Bool("groupContactsByLetter", true);
        _showDirectory = p.Bool("showContactsDirectory", true);
    }

    private void OnPrefsChanged(object? sender, EventArgs e) => Post(() =>
    {
        ReadPrefs();
        Rebuild();
    });

    private void OnSourceChanged(object? sender, EventArgs e) => Post(() => _ = RefreshAsync(CancellationToken.None));

    private void OnStoreChanged(object? sender, ContactChangedEventArgs e)
    {
        if (string.Equals(e.AccountKey, _source.AccountKey, StringComparison.Ordinal))
        {
            Post(Rebuild);
        }
    }

    private void Post(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }

    private async Task LoadDirectoryAsync()
    {
        if (_directory is null || _source.AccountKey is not { } key)
        {
            return;
        }

        try
        {
            var result = await _directory.LoadAsync(key, CancellationToken.None).ConfigureAwait(true);
            _error = result.IsOk ? null : result.Error!.Message;
            _directoryCards = result.IsOk ? [.. result.Value.Principals.Select(ContactImport.PrincipalCard)] : [];
        }
        catch (PortPendingException e)
        {
            _error = "The directory is not available yet (" + e.Message + ")";
        }

        Rebuild();
    }

    private void Changed() => OnPropertyChanged(string.Empty);

    // reads the store once and derives everything the screen shows; nothing waits, the state is complete before the single notification
    private void Rebuild()
    {
        var snapshot = _source.AccountKey is { } key ? _store.Snapshot(key) : null;
        var cards = snapshot?.Cards ?? [];
        _books = snapshot?.Books ?? [];
        _contacts = [.. cards.Where(c => !ContactsJson.IsGroup(c))];
        _groups = [.. cards.Where(ContactsJson.IsGroup)];
        _bookEntries = [.. _books.Select(BookEntry)];
        _groupEntries = [.. _groups.Select(g => new ContactsSidebarEntry(ContactsPlace.Group, ContactsJson.Id(g), ContactsListRules.FullName(g), _contacts.Count(c => ContactsJson.HasMember(g, c)))).OrderBy(e => e.Name, StringComparer.InvariantCultureIgnoreCase)];
        _categoryEntries = [.. _contacts.SelectMany(c => ContactsJson.Keys(c, "keywords")).GroupBy(k => k, StringComparer.Ordinal).Select(g => new ContactsSidebarEntry(ContactsPlace.Category, g.Key, g.Key, g.Count())).OrderBy(e => e.Name, StringComparer.InvariantCultureIgnoreCase)];
        _duplicates = FindDuplicates(_contacts);
        if (PlaceVanished())
        {
            _place = ContactsPlace.All;
            _placeId = null;
        }

        _rows = BuildRows();
        var ids = _rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        SetSelection(_selected.Where(ids.Contains));
        if (_openId is not null && CardOf(_openId) is null)
        {
            _openId = null;
        }

        Changed();
    }

    private bool PlaceVanished() => _place switch
    {
        ContactsPlace.Group => _groups.All(g => !string.Equals(ContactsJson.Id(g), _placeId, StringComparison.Ordinal)),
        ContactsPlace.Book => _books.All(b => !string.Equals(ContactsJson.Id(b), _placeId, StringComparison.Ordinal)),
        ContactsPlace.Category => _categoryEntries.All(c => !string.Equals(c.Id, _placeId, StringComparison.Ordinal)),
        _ => false,
    };

    private ContactsSidebarEntry BookEntry(JsonNode book)
    {
        var id = ContactsJson.Id(book);
        var count = _contacts.Count(c => ContactsJson.Keys(c, "addressBookIds").Contains(id, StringComparer.Ordinal));
        var isDefault = book["isDefault"] is JsonValue d && d.TryGetValue<bool>(out var flag) && flag;
        return new ContactsSidebarEntry(ContactsPlace.Book, id, ContactsJson.Str(book, "name"), count, isDefault);
    }

    private static List<IReadOnlyList<JsonNode>> FindDuplicates(IReadOnlyList<JsonNode> contacts)
    {
        try
        {
            return [.. ContactMerge.FindGroups(contacts).Select(g => (IReadOnlyList<JsonNode>)[.. g.Select(i => contacts[i])])];
        }
        catch (PortPendingException)
        {
            // con-data's S1 has not landed: no duplicates are offered
            return [];
        }
    }

    private List<ContactsRow> BuildRows()
    {
        IEnumerable<JsonNode> source = _place switch
        {
            ContactsPlace.Book => _contacts.Where(c => ContactsJson.Keys(c, "addressBookIds").Contains(_placeId ?? string.Empty, StringComparer.Ordinal)),
            ContactsPlace.Group => _groups.FirstOrDefault(g => string.Equals(ContactsJson.Id(g), _placeId, StringComparison.Ordinal)) is { } group ? _contacts.Where(c => ContactsJson.HasMember(group, c)) : [],
            ContactsPlace.Category => _contacts.Where(c => ContactsJson.Keys(c, "keywords").Contains(_placeId ?? string.Empty, StringComparer.Ordinal)),
            ContactsPlace.Directory => _directoryCards,
            ContactsPlace.Duplicates => _duplicates.SelectMany(g => g).DistinctBy(ContactsJson.Id, StringComparer.Ordinal),
            _ => _contacts,
        };
        var rows = new List<ContactsRow>();
        foreach (var card in source.Where(c => ContactsListRules.Matches(c, _search) && _filters.Matches(c)))
        {
            var name = ContactsListRules.FullName(card);
            var sort = ContactsListRules.SortName(card, _byLast);
            var email = ContactsJson.Emails(card).FirstOrDefault();
            rows.Add(new ContactsRow(_source.AccountKey ?? string.Empty, ContactsJson.Id(card), name, sort, ContactsListRules.LetterOf(sort), ContactsListRules.Initials(name, email ?? string.Empty), email, ContactsJson.PhotoUri(card), ContactsJson.IsGroup(card), ContactsJson.FirstOf(card["organizations"], "name")));
        }

        rows.Sort((a, b) => string.CompareOrdinal(ContactsListRules.Fold(a.SortKey), ContactsListRules.Fold(b.SortKey)));
        return rows;
    }
}
