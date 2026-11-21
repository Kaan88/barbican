// Mirrors: Bulwark lib/global-search/providers/{contacts,calendar,files}.ts (local: the stores' contents; remote: load what the store has not read yet). The sources are the contact store, the calendar source (the calendar store in
// the app, sample events in a hidden run) and the file store.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Core.Files;
using Barbican.Presentation.Calendar.Grid;

namespace Barbican.Presentation.Search;

/// <summary>The contacts, calendar and files of the active login, from the stores.</summary>
public sealed class StoreSearchSources(Func<string?> accountKey, IContactStore? contacts, ICalendarSource? calendar, IFileStore? files, TimeProvider time) : IGlobalSearchSources
{
    /// <summary>How far from today an event is looked for (the calendar store's own search window, StoreCalendarSource.Search).</summary>
    public const int EventWindowDays = 366;

    /// <inheritdoc />
    public async Task<GlobalSearchData> LoadAsync(CancellationToken ct)
    {
        if (accountKey() is not { Length: > 0 } key)
        {
            return GlobalSearchData.Empty;
        }

        var cardsTask = contacts is null ? Task.FromResult<ContactSnapshot?>(null) : SnapshotAsync(contacts.Snapshot(key), () => contacts.LoadAsync(key, ct));
        var eventsTask = calendar is null ? Task.FromResult((Events: (IReadOnlyList<JsonNode>)[], Calendars: (IReadOnlyList<JsonNode>)[])) : EventsAsync(calendar, key, ct);
        var filesTask = files is null || !files.Supports(key) ? Task.FromResult<FileSnapshot?>(null) : SnapshotAsync(files.Snapshot(key), () => files.LoadAsync(key, ct));
        var c = await cardsTask.ConfigureAwait(false);
        var e = await eventsTask.ConfigureAwait(false);
        var f = await filesTask.ConfigureAwait(false);
        return new GlobalSearchData
        {
            Cards = c?.Cards ?? [],
            Books = c?.Books ?? [],
            Events = e.Events,
            Calendars = e.Calendars,
            Files = f?.Entries ?? [],
        };
    }

    // a store not read yet is loaded once; a failed load leaves that kind out (the others still answer)
    private static async Task<T?> SnapshotAsync<T>(T? held, Func<Task<Result<T>>> load)
        where T : class
    {
        if (held is not null)
        {
            return held;
        }

        var r = await load().ConfigureAwait(false);
        return r.IsOk ? r.Value : null;
    }

    // one JSCalendar object per event of the window (the store's own object when it has one, else what the occurrence says), and the calendars as {id, name}
    private async Task<(IReadOnlyList<JsonNode> Events, IReadOnlyList<JsonNode> Calendars)> EventsAsync(ICalendarSource source, string key, CancellationToken ct)
    {
        if (source.Calendars(key).Count == 0)
        {
            _ = await source.LoadAsync(key, ct).ConfigureAwait(false);
        }

        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var events = new List<JsonNode>();
        foreach (var o in source.OccurrencesIn(key, today.AddDays(-EventWindowDays), today.AddDays(EventWindowDays)))
        {
            if (o.Kind != CalendarOccurrenceKind.Event || !seen.Add(o.Base))
            {
                continue;
            }

            events.Add(source.EventObject(key, o.Base) ?? Synthesize(o));
        }

        IReadOnlyList<JsonNode> calendars = [.. source.Calendars(key).Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["name"] = c.Name })];
        return (events, calendars);
    }

    private static JsonObject Synthesize(CalendarOccurrence o)
    {
        var ev = new JsonObject
        {
            ["id"] = o.Base,
            ["uid"] = o.Base,
            ["title"] = o.Title,
            ["start"] = o.Start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["showWithoutTime"] = o.AllDay,
            ["calendarIds"] = new JsonObject { [o.Calendar] = true },
        };
        if (o.Location.Length > 0)
        {
            ev["locations"] = new JsonObject { ["l1"] = new JsonObject { ["name"] = o.Location } };
        }

        return ev;
    }
}
