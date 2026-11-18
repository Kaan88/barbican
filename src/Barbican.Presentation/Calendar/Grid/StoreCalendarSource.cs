// Mirrors: Rust calendar.rs:273 load, :355-:421 occurrences_in (tasks as all-day chips with the marker prefix), :93 color_for; M4 plan section 4.2. The default source: the cal-data store and its occurrence expansion.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The <see cref="ICalendarSource"/> over <see cref="ICalendarStore"/>.</summary>
public sealed class StoreCalendarSource : ICalendarSource, IDisposable
{
    private readonly ICalendarStore _store;
    private readonly IAccountManager _accounts;

    public StoreCalendarSource(ICalendarStore store, IAccountManager accounts)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(accounts);
        _store = store;
        _accounts = accounts;
        _store.Changed += OnStoreChanged;
    }

    public event EventHandler<CalendarSourceChangedEventArgs>? Changed;

    public void Dispose() => _store.Changed -= OnStoreChanged;

    public async Task<Result<Unit>> LoadAsync(string accountKey, CancellationToken ct)
    {
        var loaded = await _store.LoadAsync(accountKey, ct).ConfigureAwait(false);
        return loaded.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(loaded.Error!);
    }

    public IReadOnlyList<CalendarListRow> Calendars(string accountKey) =>
        _store.Snapshot(accountKey) is { } snapshot ? CalendarRows.From(snapshot.Calendars, accountKey) : [];

    public IReadOnlyList<CalendarOccurrence> OccurrencesIn(string accountKey, DateOnly from, DateOnly until)
    {
        if (_store.Snapshot(accountKey) is not { } snapshot)
        {
            return [];
        }

        // the snapshot is an immutable record of the load: nothing here holds the store while the caller renders (B-CAL-048)
        var events = snapshot.Events.Where(e => !TaskOccurrences.IsTask(e)).ToList();
        var result = new List<CalendarOccurrence>(CalendarOccurrences.In(events, from, until));
        result.AddRange(TaskOccurrences.In(snapshot.Events, from, until));
        result.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.AllDay.CompareTo(a.AllDay));
        return result;
    }

    public IReadOnlyList<CalendarOccurrence> Search(string accountKey, string query, DateOnly around, int max)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Trim();
        if (q.Length == 0)
        {
            return [];
        }

        return OccurrencesIn(accountKey, around.AddDays(-366), around.AddDays(366))
            .Where(o => o.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || o.Location.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => Math.Abs((DateOnly.FromDateTime(o.Start).DayNumber) - around.DayNumber))
            .ThenBy(o => o.Start)
            .Take(max)
            .ToList();
    }

    public bool CanMove(string accountKey, string occurrenceId)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        if (occurrenceId.StartsWith("task:", StringComparison.Ordinal) || occurrenceId.StartsWith("bday:", StringComparison.Ordinal))
        {
            return false;
        }

        var baseId = occurrenceId.Split('|', 2)[0];
        if (EventObject(accountKey, baseId) is not { } ev)
        {
            return false;
        }

        var mine = _accounts.Accounts.Where(a => string.Equals(a.Key, accountKey, StringComparison.Ordinal)).Select(a => a.User).ToList();
        if (!CalendarEditability.CanMove(ev, mine))
        {
            return false;
        }

        var calendar = CalendarRows.FirstCalendar(ev);
        return Calendars(accountKey).FirstOrDefault(c => string.Equals(c.Id, calendar, StringComparison.Ordinal))?.CanEdit ?? true;
    }

    public JsonNode? EventObject(string accountKey, string eventId) =>
        _store.Snapshot(accountKey)?.Events.FirstOrDefault(e => string.Equals(e["id"]?.GetValue<string>(), eventId, StringComparison.Ordinal));

    private void OnStoreChanged(object? sender, CalendarChangedEventArgs e) => Changed?.Invoke(this, new CalendarSourceChangedEventArgs(e.AccountKey, e.CalendarsChanged));
}
