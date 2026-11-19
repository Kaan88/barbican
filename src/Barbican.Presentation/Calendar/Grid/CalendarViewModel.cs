// Mirrors: Rust calendar.rs ST (:21), range (:208), nav (:626), set_view (:649), toggle_calendar (:655), search (:660), render (:445); Bulwark stores/calendar-store.ts, components/calendar/calendar-app.tsx;
// B-CAL-006, 008, 010, 053, 055, 059; I-296..I-345; hooks onCalendarDateChange, onCalendarViewChange, onCalendarVisibilityToggle, onCalendarChange, onCalendarEventOpen (spec/plugins/hooks.csv, owner cal-views).

using System.Globalization;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Core.Settings.Pages;
using System.Text.Json.Nodes;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The default <see cref="ICalendarViewModel"/>: the view, the anchor, what the range shows, the sidebar, the selection, the search. Every change of state raises <c>PropertyChanged("")</c> once, on the UI thread, after the new state is complete.</summary>
public sealed class CalendarViewModel : ObservableObject, ICalendarViewModel, IDisposable
{
    private readonly ICalendarSource _source;
    private readonly IAccountManager _accounts;
    private readonly IPrefsStore _prefs;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;
    private readonly IDispatcher _dispatcher;
    private readonly ICalendarEditors? _editors;
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private CalendarViewKind _view;
    private string _viewPreference = string.Empty;
    private readonly ISettingValues? _settingValues;
    private Task _viewWrites = Task.CompletedTask;
    private int _pendingViewWrites;
    private DateOnly _anchor;
    private DateOnly _mini;
    private bool _narrow;
    private bool _allDayExpanded;
    private bool _loading;
    private string? _error;
    private string? _selectedId;
    private string _search = string.Empty;
    private CalendarViewSettings _settings;
    private (DateOnly From, DateOnly To) _range;
    private IReadOnlyList<CalendarOccurrence> _all = [];
    private IReadOnlyList<CalendarOccurrence> _occurrences = [];
    private IReadOnlyList<CalendarListRow> _calendars = [];
    private IReadOnlyList<CalendarOccurrence> _results = [];
    private IReadOnlyList<MiniDay> _miniDays = [];
    private string? _loadedFor;
    private bool _disposed;

    public CalendarViewModel(ICalendarSource source, IAccountManager accounts, IPrefsStore prefs, IHookBus hooks, TimeProvider time, IDispatcher dispatcher, ICalendarEditors? editors = null, ISettingValues? settingValues = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _source = source;
        _accounts = accounts;
        _prefs = prefs;
        _hooks = hooks;
        _time = time;
        _dispatcher = dispatcher;
        _editors = editors;
        _settingValues = settingValues;
        _viewPreference = prefs.Current.Str("calendarViewMode", "month");
        _view = ViewFromPreference(_viewPreference);
        _settings = CalendarViewSettings.Read(prefs.Current);
        _anchor = TodayDate;
        _mini = FirstOfMonth(_anchor);
        _range = ComputeRange();
        _source.Changed += OnSourceChanged;
        _prefs.Changed += OnPrefsChanged;
        _accounts.ActiveChanged += OnAccountChanged;
        Recompute(notify: false);
    }

    public CalendarViewKind View => _view;

    public DateOnly Anchor => _anchor;

    public (DateOnly From, DateOnly To) Range => _range;

    public string Title => CalendarFormat.Title(_view, _anchor, _range.From, _range.To, _settings.Core.ShowWeekNumbers);

    /// <summary>The smaller wording of a narrow header (B-CAL-053).</summary>
    public string ShortTitle => CalendarFormat.ShortTitle(_view, _anchor, _range.From, _range.To);

    public IReadOnlyList<CalendarOccurrence> Occurrences => _occurrences;

    public IReadOnlyList<CalendarListRow> Calendars => _calendars;

    public string? SelectedId => _selectedId;

    public string SearchText
    {
        get => _search;
        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(_search, text, StringComparison.Ordinal))
            {
                return;
            }

            _search = text;
            Recompute(notify: true);
        }
    }

    public bool IsLoading => _loading;

    public string? LoadError => _error;

    /// <summary>Today on the user's clock (the injected <see cref="TimeProvider"/>).</summary>
    public DateOnly TodayDate => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    /// <summary>Now on the user's clock.</summary>
    public DateTime Now() => _time.GetLocalNow().DateTime;

    public CalendarViewSettings Settings => _settings;

    /// <summary>The login the surface shows, or null.</summary>
    public string? AccountKey => _accounts.Active?.Key ?? _source.DefaultAccountKey;

    /// <summary>The views the switch offers: Tasks only when tasks are on and the Tasks app is not separate (Rust view_names).</summary>
    public IReadOnlyList<CalendarViewKind> AvailableViews =>
        _settings.Core.TasksEnabled && !_settings.Core.ShowTasksApp
            ? [CalendarViewKind.Day, CalendarViewKind.Week, CalendarViewKind.Month, CalendarViewKind.Agenda, CalendarViewKind.Tasks]
            : [CalendarViewKind.Day, CalendarViewKind.Week, CalendarViewKind.Month, CalendarViewKind.Agenda];

    /// <summary>The pane is narrower than 1000 px: the week shows three days unless calendarNarrowWeekAllDays (B-CAL-008).</summary>
    public bool IsNarrow => _narrow;

    /// <summary>The days the Week view shows: seven, or three in a narrow pane.</summary>
    public int WeekDaysShown => _narrow && !_settings.NarrowAllDays ? 3 : 7;

    /// <summary>The day columns of the Day and Week grids (non-working days left out when hidden, at least one column).</summary>
    public IReadOnlyList<DateOnly> Columns
    {
        get
        {
            var days = CalendarGridRules.DayColumns(_range.From, _range.To);
            if (_view == CalendarViewKind.Week && _settings.Core.HideNonWorkingDays)
            {
                var kept = days.Where(d => _settings.WorkingDays.Contains((int)d.DayOfWeek)).ToList();
                if (kept.Count > 0)
                {
                    return kept;
                }
            }

            return days;
        }
    }

    /// <summary>Hidden non-working columns (the toolbar's "hidden days" pill, I-321).</summary>
    public int HiddenDayCount => _view == CalendarViewKind.Week ? CalendarGridRules.DayColumns(_range.From, _range.To).Count - Columns.Count : 0;

    /// <summary>The all-day strip shows all its rows instead of three (G-003).</summary>
    public bool AllDayExpanded => _allDayExpanded;

    /// <summary>The first day of the month the mini month shows (follows the anchor's month, and moves on its own with its arrows).</summary>
    public DateOnly MiniMonth => _mini;

    public IReadOnlyList<MiniDay> MiniDays => _miniDays;

    /// <summary>The ISO week numbers of the six mini-month rows (the Thursday of each, Rust).</summary>
    public IReadOnlyList<int> MiniWeekNumbers
    {
        get
        {
            var start = CalendarGridRules.MonthGridStart(_mini, _settings.Core.FirstDayOfWeek);
            return Enumerable.Range(0, 6).Select(w => CalendarGridRules.WeekNumber(start.AddDays((w * 7) + 3))).ToList();
        }
    }

    /// <summary>The weekday headers from the first day of the week ("Mon" or "M").</summary>
    public IReadOnlyList<string> WeekdayNames(bool initials)
    {
        var start = CalendarGridRules.WeekStart(TodayDate, _settings.Core.FirstDayOfWeek);
        return Enumerable.Range(0, 7).Select(i =>
        {
            var n = start.AddDays(i).ToString("ddd", CultureInfo.InvariantCulture);
            return initials ? n[..1] : n;
        }).ToList();
    }

    /// <summary>The mini-month day picked for the Tasks view.</summary>
    public TaskDayFilter TaskDay { get; } = new();

    /// <summary>The occurrences matching the search text across the loaded data, nearest first (the results list under the box, I-313).</summary>
    public IReadOnlyList<CalendarOccurrence> SearchResults => _results;

    /// <summary>The selected occurrence if it is on screen.</summary>
    public CalendarOccurrence? Selected => _selectedId is null ? null : _occurrences.FirstOrDefault(o => string.Equals(o.Id, _selectedId, StringComparison.Ordinal));

    /// <summary>What the detail panel shows for the selection, or null.</summary>
    public EventDetail? Detail
    {
        get
        {
            return Selected is { } o ? DetailOf(o) : null;
        }
    }

    /// <summary>The detail of any occurrence (the hover card and the panel).</summary>
    public EventDetail DetailOf(CalendarOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        var raw = occurrence.Kind == CalendarOccurrenceKind.Birthday || AccountKey is not { } key ? null : _source.EventObject(key, occurrence.Base);
        return EventDetails.From(occurrence, raw, _calendars.FirstOrDefault(c => string.Equals(c.Id, occurrence.Calendar, StringComparison.Ordinal)), _settings.Core.TwelveHour);
    }

    public void SetView(CalendarViewKind view)
    {
        if (_view == view)
        {
            return;
        }

        _view = view;
        Recompute(notify: true);
        _hooks.Fire(HookNames.OnCalendarViewChange, () => new CalendarViewPayload(ViewName(view)));
        if (view != CalendarViewKind.Tasks && _settingValues is not null)
        {
            Interlocked.Increment(ref _pendingViewWrites);
            _viewWrites = SaveViewAsync(_viewWrites, view);
        }
    }

    public void Today()
    {
        MoveAnchor(TodayDate);
    }

    public void GoPrevious() => Step(-1);

    public void GoNext() => Step(1);

    /// <summary>The anchor goes to the day, the view stays (a mini-month pick).</summary>
    public void ShowDay(DateOnly day) => MoveAnchor(day);

    /// <summary>The Day view on that day (a month cell's day number, I-316).</summary>
    public void OpenDay(DateOnly day)
    {
        var changed = _view != CalendarViewKind.Day;
        _view = CalendarViewKind.Day;
        MoveAnchor(day, force: changed);
        if (changed)
        {
            _hooks.Fire(HookNames.OnCalendarViewChange, () => new CalendarViewPayload(ViewName(CalendarViewKind.Day)));
        }
    }

    public void SelectOccurrence(string? id)
    {
        if (string.Equals(_selectedId, id, StringComparison.Ordinal))
        {
            return;
        }

        _selectedId = id;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>A search result or a plain jump: shows the day of the occurrence and selects it.</summary>
    public void ShowOccurrence(CalendarOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        _selectedId = occurrence.Id;
        MoveAnchor(DateOnly.FromDateTime(occurrence.Start), force: true);
    }

    public void ToggleCalendar(string calendarId)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        var visible = _hidden.Remove(calendarId);
        if (!visible)
        {
            _hidden.Add(calendarId);
        }

        Recompute(notify: true);
        _hooks.Fire(HookNames.OnCalendarVisibilityToggle, () => new CalendarVisibilityPayload(calendarId, visible));
    }

    /// <summary>The pane width changed: under 1000 px the week shows three days (B-CAL-008).</summary>
    public void SetNarrow(bool narrow)
    {
        if (_narrow == narrow)
        {
            return;
        }

        _narrow = narrow;
        if (_view == CalendarViewKind.Week)
        {
            Recompute(notify: true);
        }
        else
        {
            OnPropertyChanged(nameof(IsNarrow));
        }
    }

    public void ToggleAllDay()
    {
        _allDayExpanded = !_allDayExpanded;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The mini month's own arrows (the grid does not move).</summary>
    public void MiniStep(int months)
    {
        _mini = FirstOfMonth(_mini.AddMonths(months));
        Recompute(notify: true);
    }

    /// <summary>The mini month's month and year picker (I-303).</summary>
    public void SetMiniMonth(int year, int month)
    {
        _mini = new DateOnly(Math.Clamp(year, 1, 9998), Math.Clamp(month, 1, 12), 1);
        Recompute(notify: true);
    }

    /// <summary>"yyyy-MM-dd" picks the day of the Tasks view, again lifts it, "clear" lifts it.</summary>
    public void ToggleTaskDay(string how)
    {
        TaskDay.Toggle(how);
        Recompute(notify: true);
    }

    /// <summary>Opens the occurrence: the hook, then the editors (I-317).</summary>
    public async Task OpenAsync(string occurrenceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        var o = _occurrences.FirstOrDefault(x => string.Equals(x.Id, occurrenceId, StringComparison.Ordinal)) ?? _results.FirstOrDefault(x => string.Equals(x.Id, occurrenceId, StringComparison.Ordinal));
        if (o is not null)
        {
            _hooks.Fire(HookNames.OnCalendarEventOpen, () => new CalendarEventOpenPayload(o.Base, o.RecurrenceId, o.Title, o.Start, o.End, o.AllDay, o.Calendar, o.Location));
        }

        if (_editors is null)
        {
            return;
        }

        try
        {
            await _editors.OpenAsync(occurrenceId, ct).ConfigureAwait(true);
        }
        catch (PortPendingException)
        {
            // the editors are cal-ui's: until they land, opening only selects (the detail panel shows the event)
        }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        if (AccountKey is not { } key)
        {
            _error = null;
            _loading = false;
            Recompute(notify: true);
            return;
        }

        _loading = true;
        _error = null;
        OnPropertyChanged(string.Empty);
        Result<Unit> result;
        try
        {
            result = await _source.LoadAsync(key, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            OnUi(() =>
            {
                _loading = false;
                OnPropertyChanged(string.Empty);
            });
            throw;
        }
        catch (PortPendingException e)
        {
            result = Result<Unit>.Fail(new Barbican.Core.Jmap.Errors.ValidationError(e.Message));
        }

        OnUi(() =>
        {
            _loading = false;
            _error = result.IsOk ? null : result.Error!.Message;
            _loadedFor = result.IsOk ? key : null;
            Recompute(notify: true);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Changed -= OnSourceChanged;
        _prefs.Changed -= OnPrefsChanged;
        _accounts.ActiveChanged -= OnAccountChanged;
    }

    /// <summary>The name a hook payload and the automation use for a view.</summary>
    public static string ViewName(CalendarViewKind view) => view.ToString().ToLowerInvariant();

    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);

    private void Step(int direction)
    {
        var next = _view switch
        {
            CalendarViewKind.Day => _anchor.AddDays(direction),
            CalendarViewKind.Week => _anchor.AddDays(direction * WeekDaysShown),
            CalendarViewKind.Agenda => _anchor.AddDays(direction * CalendarOccurrences.AgendaDays),
            _ => FirstOfMonth(_anchor).AddMonths(direction),
        };
        MoveAnchor(next);
    }

    private void MoveAnchor(DateOnly day, bool force = false)
    {
        if (_anchor == day && !force)
        {
            return;
        }

        var monthChanged = day.Month != _anchor.Month || day.Year != _anchor.Year;
        var changed = _anchor != day;
        _anchor = day;
        if (monthChanged || force)
        {
            _mini = FirstOfMonth(day);
        }

        Recompute(notify: true);
        if (changed)
        {
            _hooks.Fire(HookNames.OnCalendarDateChange, () => new CalendarDatePayload(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
    }

    private (DateOnly From, DateOnly To) ComputeRange()
    {
        var a = _anchor;
        switch (_view)
        {
            case CalendarViewKind.Day:
                return (a, a.AddDays(1));
            case CalendarViewKind.Week when WeekDaysShown < 7:
                return (a, a.AddDays(WeekDaysShown));
            case CalendarViewKind.Week:
                var ws = CalendarGridRules.WeekStart(a, _settings.Core.FirstDayOfWeek);
                return (ws, ws.AddDays(7));
            case CalendarViewKind.Agenda:
                return (a, a.AddDays(CalendarOccurrences.AgendaDays));
            default:
                var start = CalendarGridRules.MonthGridStart(a, _settings.Core.FirstDayOfWeek);
                return (start, start.AddDays(42));
        }
    }

    // One pass: the range, the occurrences of the visible calendars, the sidebar, the mini month and the results; then one notification.
    private void Recompute(bool notify)
    {
        _range = ComputeRange();
        var key = AccountKey;
        if (key is null)
        {
            _all = [];
            _occurrences = [];
            _calendars = [];
            _results = [];
            _miniDays = BuildMini(key);
            if (notify)
            {
                OnPropertyChanged(string.Empty);
            }

            return;
        }

        var rows = SafeCalendars(key);
        _calendars = rows.Select(r => _hidden.Contains(r.Id) ? r with { Visible = false } : r).ToList();
        _all = SafeOccurrences(key, _range.From, _range.To);
        var q = _search.Trim();
        var visible = new List<CalendarOccurrence>(_all.Count);
        foreach (var o in _all)
        {
            if (_hidden.Contains(o.Calendar) || !ShownKind(o) || (q.Length > 0 && !Matches(o, q)))
            {
                continue;
            }

            visible.Add(o);
        }

        _occurrences = visible;
        _results = q.Length == 0 ? [] : SafeSearch(key, q).Where(o => !_hidden.Contains(o.Calendar) && ShownKind(o)).ToList();
        _miniDays = BuildMini(key);
        if (_selectedId is not null && _occurrences.All(o => !string.Equals(o.Id, _selectedId, StringComparison.Ordinal)) && !_results.Any(o => string.Equals(o.Id, _selectedId, StringComparison.Ordinal)))
        {
            _selectedId = null;
        }

        if (notify)
        {
            OnPropertyChanged(string.Empty);
        }
    }

    private bool ShownKind(CalendarOccurrence o) => o.Kind switch
    {
        CalendarOccurrenceKind.Task => _settings.Core.TasksEnabled && _settings.Core.ShowTasksOnCalendar,
        CalendarOccurrenceKind.Birthday => _settings.Core.ShowBirthdayCalendar,
        _ => true,
    };

    private static bool Matches(CalendarOccurrence o, string q) => o.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || o.Location.Contains(q, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<CalendarListRow> SafeCalendars(string key)
    {
        try
        {
            return _source.Calendars(key);
        }
        catch (PortPendingException)
        {
            return [];
        }
    }

    private IReadOnlyList<CalendarOccurrence> SafeOccurrences(string key, DateOnly from, DateOnly until)
    {
        try
        {
            return _source.OccurrencesIn(key, from, until);
        }
        catch (PortPendingException e)
        {
            _error ??= e.Message;
            return [];
        }
    }

    private IReadOnlyList<CalendarOccurrence> SafeSearch(string key, string q)
    {
        try
        {
            return _source.Search(key, q, _anchor, 50);
        }
        catch (PortPendingException)
        {
            return [];
        }
    }

    private List<MiniDay> BuildMini(string? key)
    {
        var start = CalendarGridRules.MonthGridStart(_mini, _settings.Core.FirstDayOfWeek);
        var today = TodayDate;
        var end = start.AddDays(42);
        var source = key is null ? [] : (_range.From == start && _range.To == end ? _all : SafeOccurrences(key, start, end));
        var busy = new bool[42];
        foreach (var o in source)
        {
            if (o.Kind != CalendarOccurrenceKind.Event || _hidden.Contains(o.Calendar))
            {
                continue;
            }

            var first = DateOnly.FromDateTime(o.Start);
            var last = o.End > o.Start ? DateOnly.FromDateTime(o.End.AddTicks(-1)) : first;
            for (var i = Math.Max(0, first.DayNumber - start.DayNumber); i <= Math.Min(41, last.DayNumber - start.DayNumber); i++)
            {
                busy[i] = true;
            }
        }

        var days = new List<MiniDay>(42);
        for (var i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            var selected = _view == CalendarViewKind.Tasks
                ? TaskDay.Day == d
                : _view is CalendarViewKind.Day or CalendarViewKind.Week && d >= _range.From && d < _range.To;
            days.Add(new MiniDay(d, d.Month == _mini.Month && d.Year == _mini.Year, d == today, selected, busy[i]));
        }

        return days;
    }

    private void OnSourceChanged(object? sender, CalendarSourceChangedEventArgs e)
    {
        if (!string.Equals(e.AccountKey, AccountKey, StringComparison.Ordinal))
        {
            return;
        }

        OnUi(() =>
        {
            var before = _calendars;
            Recompute(notify: true);
            if (e.CalendarsChanged)
            {
                FireCalendarChanges(before, _calendars);
            }
        });
    }

    private void FireCalendarChanges(IReadOnlyList<CalendarListRow> before, IReadOnlyList<CalendarListRow> after)
    {
        var old = before.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var row in after)
        {
            if (!old.TryGetValue(row.Id, out var was))
            {
                Fire(row.Id, "created");
            }
            else if (!string.Equals(was.Name, row.Name, StringComparison.Ordinal) || !string.Equals(was.Color, row.Color, StringComparison.Ordinal) || was.IsDefault != row.IsDefault)
            {
                Fire(row.Id, "updated");
            }
        }

        var now = after.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in old.Keys.Where(id => !now.Contains(id)))
        {
            Fire(gone, "removed");
        }

        void Fire(string id, string change) => _hooks.Fire(HookNames.OnCalendarChange, () => new CalendarChangePayload(id, change));
    }

    private void OnPrefsChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        _settings = CalendarViewSettings.Read(_prefs.Current);
        var preference = _prefs.Current.Str("calendarViewMode", "month");
        if (!string.Equals(preference, _viewPreference, StringComparison.Ordinal))
        {
            _viewPreference = preference;
            if (Volatile.Read(ref _pendingViewWrites) == 0)
            {
                var view = ViewFromPreference(preference);
                if (_view != view)
                {
                    _view = view;
                    _hooks.Fire(HookNames.OnCalendarViewChange, () => new CalendarViewPayload(ViewName(view)));
                }
            }
        }

        Recompute(notify: true);
    });

    private async Task SaveViewAsync(Task previous, CalendarViewKind view)
    {
        try
        {
#pragma warning disable VSTHRD003 // previous is this view model's own write queue, awaited off the UI context
            await previous.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            var result = await _settingValues!.SetAsync("calendarViewMode", JsonValue.Create(ViewName(view)), CancellationToken.None).ConfigureAwait(false);
            if (!result.IsOk)
            {
                OnUi(() => { _error = result.Error?.Message ?? "The calendar view was not saved."; OnPropertyChanged(nameof(LoadError)); });
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingViewWrites);
        }
    }

    private static CalendarViewKind ViewFromPreference(string value) => value switch
    {
        "day" => CalendarViewKind.Day,
        "week" => CalendarViewKind.Week,
        "agenda" => CalendarViewKind.Agenda,
        _ => CalendarViewKind.Month,
    };

    private void OnAccountChanged(object? sender, ActiveAccountChangedEventArgs e) => OnUi(() =>
    {
        _selectedId = null;
        _hidden.Clear();
        _loadedFor = null;
        Recompute(notify: true);
        if (e.Current is not null)
        {
            _ = LoadAsync(CancellationToken.None);
        }
    });

    private void OnUi(Action action)
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }

    /// <summary>Whether the account's calendars were loaded in this session (the screen loads once on first show).</summary>
    public bool IsLoadedFor(string accountKey) => string.Equals(_loadedFor, accountKey, StringComparison.Ordinal);
}
