// Mirrors: M1-plan §4.3 (Rows, Selected, Total, LoadMoreCommand, SearchText, unread-first Inbox section); Rust mail.rs (load_list :665, load_more :706,
// sync :800, rebuild_list :556); spec B-SYNC-036/037, B-LST-014/016, I-119..I-156; Bulwark components/email/email-list.tsx. Owner mailcore.
//
// Rules this class keeps: the list is never re-assigned (every refresh goes through ListSync); the old rows stay on screen until the new page is
// ready (no blank, no Loading row above the list); a result that comes back after the user moved on is dropped (generation check); load more
// anchors on the last loaded message; a push delta is one insert, not a reset.

using System.Windows.Input;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Barbican.Presentation.Mail;

/// <summary>The message list of the open folder or search.</summary>
public sealed partial class MessageListViewModel : ObservableObject, IDisposable
{
    /// <summary>Load the next page when the selection is this close to the end of what is loaded.</summary>
    private const int PrefetchRows = 3;

    private readonly IActiveMail _active;
    private readonly MailEventHub _hub;
    private readonly IHookBus _hooks;
    private readonly IMessageOpener _opener;
    private readonly IPrefsStore _prefs;
    private readonly ISystemAccessibilitySource _system;
    private readonly MotionState _motion;
    private readonly TimeProvider _time;
    private readonly IListQueryShaper? _shaper;
    private readonly IListSourceResolver? _sources;
    private readonly LoadedList _loaded = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _badges = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private ListQuery? _query;
    private ListQuery? _pagingQuery;
    private MailboxRole _role;
    private MessageRowViewModel? _selected;
    private string? _selectedId;
    private int _generation;
    private int _pendingMove;
    private bool _loadingMore;
    private bool _isLoading;
    private int _total;
    private string _title = string.Empty;
    private string _searchText = string.Empty;
    private string? _error;
    private bool _canLoadMore;
    private bool _syncProblem;

    public MessageListViewModel(
        IActiveMail active,
        MailEventHub hub,
        IHookBus hooks,
        IMessageOpener opener,
        IPrefsStore prefs,
        ISystemAccessibilitySource system,
        MotionState motion,
        TimeProvider time,
        IListQueryShaper? shaper = null,
        IListSourceResolver? sources = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(time);
        _active = active;
        _hub = hub;
        _hooks = hooks;
        _opener = opener;
        _prefs = prefs;
        _system = system;
        _motion = motion;
        _time = time;
        _shaper = shaper;
        _sources = sources;
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => CanLoadMore);
        _hub.EventReceived += OnSyncEvent;
        _active.Changed += OnAccountChanged;
        _prefs.Changed += OnPrefsChanged;
    }

    /// <summary>The user asked to search (the <c>/</c> key): the view focuses its search box.</summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>The rows, headers included. Never re-assigned; only ListSync changes it.</summary>
    public BulkObservableCollection<ListItemViewModel> Rows { get; } = [];

    /// <summary>What the last sync did to <see cref="Rows"/> (tests and the headless scenario read it).</summary>
    public SyncStats LastSync { get; private set; }

    /// <summary>The open message row; setting it from the view is a click that opens the message.</summary>
    public MessageRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (value is null)
            {
                SetSelected(null);
                return;
            }

            if (!string.Equals(value.Id, _selectedId, StringComparison.Ordinal))
            {
                Select(value, OpenReason.Click, ListDirection.None);
            }
        }
    }

    /// <summary>How many messages the whole list holds.</summary>
    public int Total
    {
        get => _total;
        private set => SetProperty(ref _total, value);
    }

    /// <summary>A first page is on its way (the old rows stay until it arrives).</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>An account is on screen without its session yet (no network at start, Fable review 2 R2-02): "Connecting…", not "No messages".</summary>
    public bool IsConnecting => _active.AccountKey is not null && _active.Mail is null;

    /// <summary>More messages follow what is loaded.</summary>
    public bool CanLoadMore
    {
        get => _canLoadMore;
        private set
        {
            if (SetProperty(ref _canLoadMore, value))
            {
                (LoadMoreCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>The last failure of a load, shown as a status; null when the last load worked.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>What the search box holds; <see cref="SearchAsync"/> runs it.</summary>
    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value ?? string.Empty);
    }

    /// <summary>The id of the open folder, or null for a search over everything.</summary>
    public string? MailboxId => _query?.MailboxId;

    public ICommand LoadMoreCommand { get; }

    /// <summary>The messages loaded, in list order (headers left out).</summary>
    public IEnumerable<MessageRowViewModel> Messages => Rows.OfType<MessageRowViewModel>();

    /// <summary>Opens a folder: the first page replaces the rows once it has arrived; the previous load is cancelled.</summary>
    public async Task OpenFolderAsync(string mailboxId, MailboxRole role, string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(mailboxId);
        var previous = _query?.MailboxId;
        _role = role;
        Title = title ?? string.Empty;
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        var settings = ReadSettings();
        await LoadFirstPageAsync(new ListQuery { MailboxId = mailboxId, PageSize = settings.PageSize }, folderOpened: true).ConfigureAwait(true);
        if (_hooks.HasHandlers(HookNames.OnMailboxChange))
        {
            var payload = new MailboxChangedPayload(mailboxId, role.ToString().ToLowerInvariant(), previous);
            _hooks.Fire(HookNames.OnMailboxChange, () => payload);
        }
    }

    /// <summary>Runs the search box's text over the open folder; empty text goes back to the folder.</summary>
    public async Task SearchAsync(string text)
    {
        var folder = _query?.MailboxId;
        var trimmed = (text ?? string.Empty).Trim();
        _searchText = text ?? string.Empty;
        OnPropertyChanged(nameof(SearchText));
        if (trimmed.Length == 0)
        {
            if (folder is not null)
            {
                await LoadFirstPageAsync(new ListQuery { MailboxId = folder, PageSize = ReadSettings().PageSize }, folderOpened: false).ConfigureAwait(true);
            }

            return;
        }

        var shaped = await _hooks.ShapeAsync(HookNames.OnSearch, new SearchPayload(trimmed), CancellationToken.None).ConfigureAwait(true);
        await LoadFirstPageAsync(new ListQuery { MailboxId = folder, SearchText = shaped.Query, PageSize = ReadSettings().PageSize }, folderOpened: false).ConfigureAwait(true);
    }

    /// <summary>Loads the next page after the last loaded message (anchor paging) and appends it in place.</summary>
    public async Task LoadMoreAsync()
    {
        var query = _pagingQuery ?? _query;
        var cursor = _loaded.Next;
        var mail = _active.Mail;
        if (query is null || cursor is null || mail is null || _loadingMore || IsLoading)
        {
            return;
        }

        _loadingMore = true;
        var generation = _generation;
        var ct = _cts?.Token ?? CancellationToken.None;
        try
        {
            var result = await QueryPageAsync(mail, query, cursor, ct).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            if (!result.IsOk || result.Value is null)
            {
                Error = result.Error?.Message ?? "Could not load more.";
                _pendingMove = 0;
                return;
            }

            _loaded.Append(result.Value);
            await RebuildWithBadgesAsync().ConfigureAwait(true);
            Total = _loaded.Total;
            CanLoadMore = _loaded.Next is not null;
            Error = null;
            ResumePendingMove();
        }
        catch (OperationCanceledException)
        {
            // the folder changed under it
        }
        finally
        {
            _loadingMore = false;
        }
    }

    /// <summary>Selects and opens a message row.</summary>
    public void Select(MessageRowViewModel row, OpenReason reason, ListDirection direction)
    {
        ArgumentNullException.ThrowIfNull(row);
        SetSelected(row);
        if (_hooks.HasHandlers(HookNames.OnEmailSelectionChange))
        {
            var ids = new[] { row.Id };
            _hooks.Fire(HookNames.OnEmailSelectionChange, () => new SelectionChangedPayload(ids));
        }

        _opener.Open(row, reason, direction);
        if (reason == OpenReason.Key)
        {
            _motion.Step();
        }
    }

    /// <summary>Moves the selection one message up or down and opens it (j/k, the arrows). At the end of what is loaded the next page is fetched and the move completes when it arrives.</summary>
    public bool MoveSelection(int delta, OpenReason reason = OpenReason.Key)
    {
        var rows = Messages.ToList();
        if (rows.Count == 0 || delta == 0)
        {
            return false;
        }

        var at = _selectedId is null ? -1 : rows.FindIndex(r => string.Equals(r.Id, _selectedId, StringComparison.Ordinal));
        var target = at < 0 ? (delta > 0 ? 0 : rows.Count - 1) : at + delta;
        if (target >= rows.Count)
        {
            if (CanLoadMore)
            {
                _pendingMove = delta;
                _ = LoadMoreAsync();
                return true;
            }

            target = rows.Count - 1;
        }

        target = Math.Max(target, 0);
        if (target == at)
        {
            return false;
        }

        Select(rows[target], reason, delta > 0 ? ListDirection.Down : ListDirection.Up);
        if (CanLoadMore && target >= rows.Count - PrefetchRows)
        {
            _ = LoadMoreAsync();
        }

        return true;
    }

    /// <summary>Selects the first loaded message.</summary>
    public bool SelectFirst(OpenReason reason = OpenReason.Key)
    {
        var first = Messages.FirstOrDefault();
        if (first is null)
        {
            return false;
        }

        Select(first, reason, ListDirection.Up);
        return true;
    }

    /// <summary>Selects the last loaded message (Shift+G); the next page starts loading when there is one.</summary>
    public bool SelectLast(OpenReason reason = OpenReason.Key)
    {
        var last = Messages.LastOrDefault();
        if (last is null)
        {
            return false;
        }

        Select(last, reason, ListDirection.Down);
        if (CanLoadMore)
        {
            _ = LoadMoreAsync();
        }

        return true;
    }

    /// <summary>Opens the selected message again (Enter, o).</summary>
    public bool OpenSelected(OpenReason reason = OpenReason.Key)
    {
        if (_selected is null)
        {
            return false;
        }

        _opener.Open(_selected, reason, ListDirection.None);
        return true;
    }

    /// <summary>Closes the reader and clears the selection (Escape).</summary>
    public void CloseReader()
    {
        SetSelected(null);
        _opener.Close();
    }

    /// <summary>The user pressed <c>/</c>.</summary>
    public void RequestSearchFocus() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Loads the open list again, as many rows as were loaded, keeping the rows on screen until it is ready (a refused delta, a new account
    /// state). The selection stays when its message is still in the list; when it is not, the reader closes (Fable F-14).
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_query is { } q)
        {
            var hadSelection = _selectedId is not null;
            await LoadFirstPageAsync(q, folderOpened: false, rows: _loaded.Emails.Count).ConfigureAwait(true);
            if (hadSelection && _selectedId is null)
            {
                _opener.Close();
            }
        }
    }

    public void Dispose()
    {
        _hub.EventReceived -= OnSyncEvent;
        _active.Changed -= OnAccountChanged;
        _prefs.Changed -= OnPrefsChanged;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private MailListSettings ReadSettings() => MailListSettings.Read(_prefs.Current, _system.Current);

    private async Task LoadFirstPageAsync(ListQuery query, bool folderOpened, int rows = 0)
    {
        var generation = ++_generation;
        var old = _cts;
        _cts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();
        var ct = _cts.Token;
        _pendingMove = 0;
        _query = query;
        OnPropertyChanged(nameof(MailboxId));
        var mail = _active.Mail;
        if (mail is null)
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            var result = await FirstPageAsync(mail, query, rows, ct).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            IsLoading = false;
            if (!result.IsOk || result.Value is null)
            {
                Error = result.Error?.Message ?? "Could not load the list.";
                return;
            }

            await ApplyFirstPageAsync(result.Value, query, folderOpened, generation).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // replaced by a newer load
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// One page through the list seams (M2 plan 4.4): the shaper may adjust the query and the resolver may pick another source. Without them (the M1 default,
    /// and every M1 test) it is exactly <c>mail.QueryAsync(query, after, ct)</c>.
    /// </summary>
    private Task<Core.Common.Result<ListPage>> QueryPageAsync(Core.Mail.IMailService mail, ListQuery query, ListCursor? after, CancellationToken ct)
    {
        var shaped = _shaper is null ? query : _shaper.Shape(query);
        var source = _sources?.Resolve(shaped) ?? new MailServiceListSource(mail);
        return source.QueryAsync(shaped, after, ct);
    }

    /// <summary>
    /// The first page. The unread-first Inbox is two queries, the newest unread messages (<c>notKeyword $seen</c>, the section size) and
    /// the read ones (<c>hasKeyword $seen</c>), as Rust <c>fetch_unread_first</c> (mail.rs:599) does, so an Inbox with more unread
    /// messages than one page still shows its unread section; otherwise one query of <paramref name="rows"/> or the page size.
    /// </summary>
    private async Task<Core.Common.Result<ListPage>> FirstPageAsync(Core.Mail.IMailService mail, ListQuery query, int rows, CancellationToken ct)
    {
        var size = Math.Max(query.PageSize, rows);
        var settings = ReadSettings();
        if (!IsUnreadFirst(query, settings))
        {
            return await QueryPageAsync(mail, query with { PageSize = size }, null, ct).ConfigureAwait(true);
        }

        var unread = await QueryPageAsync(mail, query with { NotKeyword = "$seen", PageSize = settings.UnreadSectionSize }, null, ct).ConfigureAwait(true);
        if (!unread.IsOk || unread.Value is null)
        {
            return unread;
        }

        var read = await QueryPageAsync(mail, query with { HasKeyword = "$seen", PageSize = size }, null, ct).ConfigureAwait(true);
        if (!read.IsOk || read.Value is null)
        {
            return read;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = unread.Value.Items.Concat(read.Value.Items).Where(e => seen.Add(e.Id)).ToList();
        var threads = unread.Value.Threads.Concat(read.Value.Threads).GroupBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        return Core.Common.Result<ListPage>.Ok(new ListPage
        {
            Items = items,
            Total = unread.Value.Items.Count + (read.Value.Total ?? read.Value.Items.Count),
            Next = read.Value.Next,
            Threads = threads,
            Position = 0,
        });
    }

    private bool IsUnreadFirst(ListQuery query, MailListSettings settings) =>
        settings.UnreadFirst && _role == MailboxRole.Inbox && string.IsNullOrWhiteSpace(query.SearchText);

    private async Task ApplyFirstPageAsync(ListPage page, ListQuery query, bool folderOpened, int generation)
    {
        var items = await _hooks.ShapeAsync(
            HookNames.OnEmailsFetched,
            new EmailsFetchedPayload(page.Items, query.MailboxId),
            CancellationToken.None).ConfigureAwait(true);
        if (generation != _generation)
        {
            return;
        }

        if (query.SearchText is { Length: > 0 } text && _hooks.HasHandlers(HookNames.OnSearchResults))
        {
            var ids = await _hooks.ShapeAsync(
                HookNames.OnSearchResults,
                new SearchResultsPayload(text, [.. items.Emails.Select(e => e.Id)]),
                CancellationToken.None).ConfigureAwait(true);
            items = items with { Emails = Reorder(items.Emails, ids.EmailIds) };
        }

        var unreadFirst = IsUnreadFirst(query, ReadSettings());
        _pagingQuery = unreadFirst ? query with { HasKeyword = "$seen" } : null;
        _loaded.Replace(page with { Items = items.Emails });
        _badges.Clear();
        Error = null;
        await RebuildWithBadgesAsync().ConfigureAwait(true);
        Total = _loaded.Total;
        CanLoadMore = _loaded.Next is not null;
        // the page's query, email and thread states let the next push bring the list up to date with a delta; Watch(query) alone keeps
        // no states, so every push would reset the list (Rust mail.rs:34 states_of, :759 sync)
        if (unreadFirst)
        {
            // two queries carry no sync states: a push resets the list, which reads it again (Rust mail.rs:597)
            _active.Sync?.Watch(query);
        }
        else
        {
            _active.Sync?.Watch(query, page);
        }

        if (folderOpened)
        {
            SetSelected(null);
        }
    }

    private static List<EmailSummary> Reorder(IReadOnlyList<EmailSummary> emails, IReadOnlyList<string> ids)
    {
        var byId = emails.ToDictionary(e => e.Id, StringComparer.Ordinal);
        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    private async Task RebuildWithBadgesAsync()
    {
        RebuildRows();
        if (_hooks.HasHandlers(HookNames.OnEmailListItemRender))
        {
            await ApplyBadgesAsync().ConfigureAwait(true);
        }
    }

    private void RebuildRows()
    {
        var settings = ReadSettings();
        var context = settings.RowContextAt(_time.GetUtcNow(), _time.LocalTimeZone);
        var searching = !string.IsNullOrWhiteSpace(_query?.SearchText);
        int? unreadFirst = settings.UnreadFirst && _role == MailboxRole.Inbox && !searching ? settings.UnreadSectionSize : null;
        var rows = ListLayout.Build(_loaded.Emails, context, settings.GroupByDate, unreadFirst, _badges);
        LastSync = ListSync.SyncBy(Rows, rows, r => r.Key, (a, b) => a.SameAs(b));
        KeepSelection();
        ApplyChecks();
    }

    private async Task ApplyBadgesAsync()
    {
        var changed = false;
        foreach (var email in _loaded.Emails.Take(50))
        {
            var payload = await _hooks.ShapeAsync(HookNames.OnEmailListItemRender, new ListItemRenderPayload(email, []), CancellationToken.None).ConfigureAwait(true);
            if (payload.Badges.Count > 0)
            {
                _badges[email.Id] = payload.Badges;
                changed = true;
            }
        }

        if (changed)
        {
            RebuildRows();
        }
    }

    private void KeepSelection()
    {
        if (_selectedId is null)
        {
            return;
        }

        var row = Messages.FirstOrDefault(r => string.Equals(r.Id, _selectedId, StringComparison.Ordinal));
        if (row is null)
        {
            SetSelected(null);
        }
        else if (!ReferenceEquals(row, _selected))
        {
            _selected = row;
            OnPropertyChanged(nameof(Selected));
        }
    }

    private void SetSelected(MessageRowViewModel? row)
    {
        _selectedId = row?.Id;
        if (!ReferenceEquals(_selected, row))
        {
            _selected = row;
            OnPropertyChanged(nameof(Selected));
            SelectedTouched();
        }
    }

    private void ResumePendingMove()
    {
        var delta = _pendingMove;
        _pendingMove = 0;
        if (delta != 0)
        {
            MoveSelection(delta);
        }
    }

    private void OnSyncEvent(object? sender, MailSyncEventArgs args)
    {
        if (args.AccountKey is { } from && !string.Equals(from, _active.AccountKey, StringComparison.Ordinal))
        {
            // an event of the account that was active before a switch, still on its way
            return;
        }

        switch (args.Event)
        {
            case ListDeltaEvent when IsLoading || _query is null:
                // a delta that was computed for the list that is being replaced (a folder switch in flight) must not touch the new rows: the
                // first page being loaded is already current
                break;
            case ListDeltaEvent { Query: { } q } when q != _query:
                // a delta of a list this view no longer shows (the engine names the watched query)
                break;
            case ListResetEvent { Query: { } q } when q != _query:
                break;
            case ListDeltaEvent d:
                _loaded.Apply(d.Delta);
                _ = RebuildThenUpdateTotalAsync();
                break;
            case EmailsChangedEvent c when _loaded.Apply(c):
                _ = RebuildThenUpdateTotalAsync();
                break;
            case ListResetEvent:
                _ = ReloadAsync();
                break;
            case SyncProblemEvent problem:
                // live updates stopped for a reason the user can act on (Fable F-15)
                _syncProblem = true;
                Error = problem.Message;
                break;
            case PushStateEvent { Connected: true } when _syncProblem:
                _syncProblem = false;
                Error = null;
                break;
            default:
                break;
        }
    }

    private async Task RebuildThenUpdateTotalAsync()
    {
        RebuildRows();
        Total = _loaded.Total;
        if (_hooks.HasHandlers(HookNames.OnEmailListItemRender))
        {
            await ApplyBadgesAsync().ConfigureAwait(true);
        }
    }

    private void OnAccountChanged(object? sender, EventArgs e)
    {
        // another account: nothing of the old one stays, and its loads are cancelled
        _generation++;
        _cts?.Cancel();
        _loaded.Replace(new ListPage());
        _query = null;
        _pagingQuery = null;
        LastSync = ListSync.SyncBy(Rows, [], r => r.Key, (a, b) => a.SameAs(b));
        SetSelected(null);
        Total = 0;
        CanLoadMore = false;
        IsLoading = false;
        _opener.Close();
        OnPropertyChanged(nameof(IsConnecting));
    }

    private void OnPrefsChanged(object? sender, EventArgs e)
    {
        if (_query is not null)
        {
            RebuildRows();
        }
    }
}
