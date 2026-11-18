#if AUTOMATION
// Mirrors: M4 plan section 6.3 (screenshots with sample data; "until cal-data lands cal-views paints occurrence literals"; the drag scenario on a fake with a failing second drag). A sample-data run (BARBICAN_SAMPLE=1) has no calendar server:
// this in-memory source and writer stand in for the store and ICalendarWriter.MoveAsync, so a hidden run can show a week with overlapping events and a crowded all-day strip, drag an event and see it move, and see a failed drag roll back.
// Never part of a shipped build. The data follows design/mockups/BNCalendar.dc.html (Stand-up, Lunch with Ada, Focus, Design review with guests) relative to the clock's week.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Calendar.Grid;

#pragma warning disable MA0051 // the seed is one block of sample data

namespace Barbican.App.Views.Calendar.Automation;

/// <summary>In-memory calendars and events for sample runs; also the writer for drags.</summary>
public sealed class SampleCalendarSource : ICalendarSource, ICalendarWriter
{
    private readonly Lock _gate = new();
    private readonly List<Entry> _events = [];
    private readonly TimeProvider _time;

    private sealed record Entry(string Id, string Title, DateTime Start, DateTime End, bool AllDay, string Calendar, string Location, CalendarOccurrenceKind Kind, string? SeriesId = null, JsonNode? Raw = null);

    public SampleCalendarSource(TimeProvider time)
    {
        _time = time;
        Seed();
    }

    public event EventHandler<CalendarSourceChangedEventArgs>? Changed;

    public string? DefaultAccountKey => "sample";

    /// <summary>The next drag is refused by the "server" (the rollback scenario).</summary>
    public bool FailNext { get; set; }

    /// <summary>Moves that reached the writer: id, new start, duration (the scenario reads it).</summary>
    #pragma warning disable MA0016 // the scenario appends and reads it
    public List<string> Writes { get; } = [];
    #pragma warning restore MA0016

    public IReadOnlyList<CalendarListRow> Rows { get; } =
    [
        new("personal", "sample", "Personal", "#f0577a", true, true, false, false, true),
        new("work", "sample", "Work", "#60a5fa", true, false, false, false, true),
        new("family", "sample", "Family", "#fbbf24", true, false, false, false, true),
        new("holidays", "sample", "Holidays · TR", "#34d399", true, false, false, true, false),
    ];

    public Task<Result<Unit>> LoadAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));

    public IReadOnlyList<CalendarListRow> Calendars(string accountKey) => Rows;

    public IReadOnlyList<CalendarOccurrence> OccurrencesIn(string accountKey, DateOnly from, DateOnly until)
    {
        lock (_gate)
        {
            return [.. _events
                .Where(e => DateOnly.FromDateTime(e.Start) < until && (e.End > e.Start ? DateOnly.FromDateTime(e.End.AddTicks(-1)) : DateOnly.FromDateTime(e.Start)) >= from)
                .Select(ToOccurrence)
                .OrderBy(o => o.Start)
                .ThenByDescending(o => o.AllDay)];
        }
    }

    public IReadOnlyList<CalendarOccurrence> Search(string accountKey, string query, DateOnly around, int max)
    {
        lock (_gate)
        {
            return [.. _events
                .Where(e => e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Location.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => Math.Abs(DateOnly.FromDateTime(e.Start).DayNumber - around.DayNumber))
                .Take(max)
                .Select(ToOccurrence)];
        }
    }

    public bool CanMove(string accountKey, string occurrenceId)
    {
        lock (_gate)
        {
            var e = _events.FirstOrDefault(x => string.Equals(x.Id, occurrenceId, StringComparison.Ordinal));
            return e is { Kind: CalendarOccurrenceKind.Event } && (e.Raw is null || CalendarEditability.CanMove(e.Raw, ["sample@example.org"]));
        }
    }

    public JsonNode? EventObject(string accountKey, string eventId)
    {
        lock (_gate)
        {
            return _events.FirstOrDefault(e => string.Equals(e.Id, eventId, StringComparison.Ordinal))?.Raw;
        }
    }

    public async Task<Result<Unit>> MoveAsync(string accountKey, string occurrenceId, DateTime newStart, TimeSpan duration, CancellationToken ct)
    {
        Entry old;
        lock (_gate)
        {
            var i = _events.FindIndex(e => string.Equals(e.Id, occurrenceId, StringComparison.Ordinal));
            if (i < 0)
            {
                return Result<Unit>.Fail(new ValidationError("That event is gone."));
            }

            old = _events[i];
            _events[i] = old with { Start = newStart, End = newStart + duration };
            Writes.Add($"{occurrenceId} {newStart:yyyy-MM-ddTHH:mm} {duration.TotalMinutes:0}");
        }

        // the optimistic patch is on screen now (Patch + Changed); the "server" answers a moment later
        Changed?.Invoke(this, new CalendarSourceChangedEventArgs("sample", false));
        await Task.Delay(60, ct).ConfigureAwait(true);
        if (FailNext)
        {
            FailNext = false;
            lock (_gate)
            {
                var i = _events.FindIndex(e => string.Equals(e.Id, occurrenceId, StringComparison.Ordinal));
                _events[i] = old;
            }

            Changed?.Invoke(this, new CalendarSourceChangedEventArgs("sample", false));
            return Result<Unit>.Fail(new TransportError("the sample server refused the change", false));
        }

        return Result<Unit>.Ok(default);
    }

    public Task<Result<string>> SaveEventAsync(EventWrite write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (write.EventId is { } id && PatchFrom(id, write.Data))
        {
            return Task.FromResult(Result<string>.Ok(id));
        }

        return Task.FromResult(Result<string>.Ok(AddEventFrom(write.Data)));
    }

    public Task<Result<Unit>> DeleteEventAsync(string accountKey, string eventId, EditScope scope, string? recurrenceId, bool notify, CancellationToken ct) =>
        Task.FromResult(RemoveId(eventId) ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("That event is gone.")));

    public Task<Result<string>> CreateAsync(string accountKey, string calendarId, JsonObject data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Task.FromResult(Result<string>.Ok(string.Equals((string?)data["@type"], "Task", StringComparison.Ordinal) ? AddTaskFrom(calendarId, data) : AddEventFrom(data)));
    }

    public Task<Result<Unit>> UpdateAsync(string accountKey, string id, JsonObject patch, CancellationToken ct) =>
        Task.FromResult(PatchFrom(id, patch) ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("That item is gone.")));

    public Task<Result<Unit>> DestroyAsync(string accountKey, string id, CancellationToken ct) =>
        Task.FromResult(RemoveId(id) ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("That item is gone.")));

    /// <summary>Adds many tasks and events (the L-027 probe: thousands open in under a second).</summary>
    public void AddBulk(int tasks, int events)
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        lock (_gate)
        {
            for (var i = 0; i < tasks; i++)
            {
                var day = today.AddDays(i % 28);
                var start = day.ToDateTime(TimeOnly.MinValue);
                _events.Add(new Entry("task:bulk" + i, "○ Task " + i, start, start.AddDays(1), true, "personal", string.Empty, CalendarOccurrenceKind.Task));
            }

            for (var i = 0; i < events; i++)
            {
                var start = today.AddDays(i % 28).ToDateTime(new TimeOnly(8 + (i % 10), (i % 4) * 15));
                _events.Add(new Entry("bulk" + i, "Meeting " + i, start, start.AddMinutes(30 + ((i % 3) * 30)), false, i % 2 == 0 ? "work" : "personal", string.Empty, CalendarOccurrenceKind.Event));
            }
        }

        Changed?.Invoke(this, new CalendarSourceChangedEventArgs("sample", false));
    }

    // ---- the editors' side (cal-ui reads JSCalendar objects and writes through ICalendarWriter): the same entries, as JSON ----

    private const string Done = "✓";

    /// <summary>The calendars as JSCalendar objects (the editors' source).</summary>
    public IReadOnlyList<JsonNode> CalendarObjects() =>
        [.. Rows.Select(r => (JsonNode)new JsonObject
        {
            ["id"] = r.Id,
            ["name"] = r.Name,
            ["color"] = r.Color,
            ["isDefault"] = r.IsDefault,
            ["isVisible"] = true,
            ["myRights"] = new JsonObject { ["mayReadItems"] = true, ["mayWriteAll"] = r.CanEdit, ["mayDelete"] = r.CanEdit },
        })];

    /// <summary>Events and tasks as JSCalendar objects; an entry with a raw object keeps it, the others get title, start and duration.</summary>
    public IReadOnlyList<JsonNode> EventObjects()
    {
        lock (_gate)
        {
            var list = new List<JsonNode>();
            foreach (var e in _events)
            {
                if (e.Kind == CalendarOccurrenceKind.Task)
                {
                    var id = e.Id.StartsWith("task:", StringComparison.Ordinal) ? e.Id[5..] : e.Id;
                    list.Add(new JsonObject
                    {
                        ["@type"] = "Task",
                        ["id"] = id,
                        ["title"] = TaskTitle(e.Title),
                        ["due"] = e.Start.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                        ["showWithoutTime"] = true,
                        ["progress"] = e.Title.StartsWith(Done, StringComparison.Ordinal) ? "completed" : "needs-action",
                        ["calendarIds"] = new JsonObject { [e.Calendar] = true },
                    });
                    continue;
                }

                if (e.SeriesId is not null)
                {
                    continue; // the stand-up series is shown on the grid only
                }

                var json = e.Raw?.DeepClone() as JsonObject ?? [];
                json["@type"] = "Event";
                json["id"] = e.Id;
                json["title"] = e.Title;
                json["start"] = e.Start.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                json["duration"] = System.Xml.XmlConvert.ToString(e.End - e.Start);
                json["timeZone"] = "UTC";
                json["showWithoutTime"] = e.AllDay;
                json["calendarIds"] = new JsonObject { [e.Calendar] = true };
                if (e.Location.Length > 0)
                {
                    json["locations"] = new JsonObject { ["l1"] = new JsonObject { ["name"] = e.Location } };
                }

                list.Add(json);
            }

            return list;
        }
    }

    private static string TaskTitle(string marked) => marked.TrimStart('✓', '○', ' ');

    /// <summary>A new event from the editor's data (start, duration, title, calendar); returns its id.</summary>
    public string AddEventFrom(JsonObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var start = DateTime.Parse((string?)data["start"] ?? "2026-01-01T09:00:00", System.Globalization.CultureInfo.InvariantCulture);
        var span = data["duration"] is JsonValue d && d.TryGetValue<string>(out var text) ? System.Xml.XmlConvert.ToTimeSpan(text) : TimeSpan.FromHours(1);
        var calendar = data["calendarIds"] is JsonObject c && c.FirstOrDefault().Key is { } k ? k : "personal";
        var id = "new" + Guid.NewGuid().ToString("N")[..6];
        var location = data["locations"] is JsonObject l && l.FirstOrDefault().Value?["name"] is { } n ? (string?)n ?? string.Empty : string.Empty;
        lock (_gate)
        {
            _events.Add(new Entry(id, (string?)data["title"] ?? "(no title)", start, start + span, (bool?)data["showWithoutTime"] == true, calendar, location, CalendarOccurrenceKind.Event, Raw: data.DeepClone()));
        }

        Changed?.Invoke(this, new CalendarSourceChangedEventArgs("sample", false));
        return id;
    }

    /// <summary>A new task from the task editor's or the quick-add data.</summary>
    public string AddTaskFrom(string calendarId, JsonObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var due = data["due"] is JsonValue d && d.TryGetValue<string>(out var text) && DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed.Date : _time.GetLocalNow().Date;
        var id = "newtask" + Guid.NewGuid().ToString("N")[..6];
        lock (_gate)
        {
            _events.Add(new Entry("task:" + id, "○ " + ((string?)data["title"] ?? "(untitled task)"), due, due.AddDays(1), true, calendarId, string.Empty, CalendarOccurrenceKind.Task));
        }

        Changed?.Invoke(this, new CalendarSourceChangedEventArgs("sample", false));
        return id;
    }

    /// <summary>A task patch (title, progress, due) or an event title patch.</summary>
    public bool PatchFrom(string id, JsonObject patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        lock (_gate)
        {
            var i = _events.FindIndex(e => string.Equals(e.Id, id, StringComparison.Ordinal) || string.Equals(e.Id, "task:" + id, StringComparison.Ordinal));
            if (i < 0)
            {
                return false;
            }

            var e = _events[i];
            var marker = e.Title.StartsWith(Done, StringComparison.Ordinal) ? Done + " " : "○ ";
            var title = e.Kind == CalendarOccurrenceKind.Task ? TaskTitle(e.Title) : e.Title;
            foreach (var (key, value) in patch)
            {
                switch (key)
                {
                    case "title":
                        title = (string?)value ?? title;
                        break;
                    case "progress":
                        marker = string.Equals((string?)value, "completed", StringComparison.Ordinal) ? Done + " " : "○ ";
                        break;
                    case "due":
                        if (value is JsonValue v && v.TryGetValue<string>(out var text) && DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var due))
                        {
                            e = e with { Start = due.Date, End = due.Date.AddDays(1) };
                        }

                        break;
                    default:
                        break;
                }
            }

            _events[i] = e with { Title = e.Kind == CalendarOccurrenceKind.Task ? marker + title : title };
        }

        Changed?.Invoke(this, new CalendarSourceChangedEventArgs("sample", false));
        return true;
    }

    /// <summary>Removes an event or task by id.</summary>
    public bool RemoveId(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _events.RemoveAll(e => string.Equals(e.Id, id, StringComparison.Ordinal) || string.Equals(e.Id, "task:" + id, StringComparison.Ordinal)) > 0;
        }

        if (removed)
        {
            Changed?.Invoke(this, new CalendarSourceChangedEventArgs("sample", false));
        }

        return removed;
    }

    private static CalendarOccurrence ToOccurrence(Entry e) =>
        new(e.Id, e.SeriesId ?? e.Id.Split('|')[0], e.Id.Contains('|', StringComparison.Ordinal) ? e.Id.Split('|')[1] : null, e.Title, e.Start, e.End, e.AllDay, e.Calendar, e.Location, e.Kind, e.Title.StartsWith('✓'));

    private void Seed()
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var monday = CalendarGridRules.WeekStart(today, 1);
        DateTime At(int day, int hour, int minute = 0) => monday.AddDays(day).ToDateTime(new TimeOnly(hour, minute));
        void Add(string id, string title, DateTime start, DateTime end, string calendar, string location = "", JsonNode? raw = null) =>
            _events.Add(new Entry(id, title, start, end, false, calendar, location, CalendarOccurrenceKind.Event, Raw: raw));
        void AllDay(string id, string title, int firstDay, int lastDay, string calendar) =>
            _events.Add(new Entry(id, title, monday.AddDays(firstDay).ToDateTime(TimeOnly.MinValue), monday.AddDays(lastDay + 1).ToDateTime(TimeOnly.MinValue), true, calendar, string.Empty, CalendarOccurrenceKind.Event));

        // the week of the mockup
        Add("lunch-ada", "Lunch with Ada", At(0, 13), At(0, 14), "family");
        Add("focus-identity", "Focus: identity", At(0, 15), At(0, 17), "holidays");
        Add("q4", "Q4 numbers with Jonas", At(1, 9), At(1, 10, 30), "work");
        Add("dentist", "Dentist", At(1, 14), At(1, 15), "personal");
        Add("focus-release", "Focus: Barbican release", At(2, 11), At(2, 14), "holidays");
        Add("sync-a", "Roadmap sync", At(2, 11), At(2, 12), "work");
        Add("sync-b", "1:1 with Mira", At(2, 11, 30), At(2, 12, 30), "work");
        Add("flight", "Flight TP668", At(3, 8), At(3, 9, 15), "personal", "LIS → AMS");
        Add("design-review", "Design review", At(3, 14), At(3, 15, 30), "family", "Room 4B, Studio North", JsonNode.Parse("""
            {"id": "design-review", "title": "Design review", "participants": {
              "p1": {"name": "Mira Okafor", "email": "mira@example.org", "roles": {"owner": true}, "participationStatus": "accepted"},
              "p2": {"name": "Jonas Weber", "email": "jonas@example.org", "participationStatus": "accepted"},
              "p3": {"name": "You", "email": "sample@example.org", "participationStatus": "needs-action"},
              "p4": {"name": "Lena Vos", "email": "lena@example.org", "participationStatus": "tentative"}}}
            """));
        Add("drinks", "Drinks, Studio North", At(4, 16), At(4, 18), "personal", "Studio North");
        Add("sunday-dinner", "Sunday dinner", At(6, 12), At(6, 14), "family");
        for (var d = -35; d < 70; d++)
        {
            var day = monday.AddDays(d);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var start = day.ToDateTime(new TimeOnly(10, 30));
            _events.Add(new Entry("standup|" + start.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), "Stand-up", start, start.AddMinutes(30), false, "work", "Room 2", CalendarOccurrenceKind.Event, "standup"));
        }

        // a crowded all-day strip (the cap at three rows)
        AllDay("trip", "Trip to Lisbon", 1, 3, "personal");
        AllDay("release", "Release day", 2, 2, "work");
        AllDay("bank", "Bank holiday", 2, 2, "holidays");
        AllDay("bday", "Ada's birthday", 2, 2, "family");
        AllDay("offsite", "Team offsite", 2, 3, "work");

        // a few events in the other weeks of the month
        for (var w = 1; w <= 4; w++)
        {
            Add("review-" + w, "Sprint review", At((7 * w) + 2, 15), At((7 * w) + 2, 16), "work");
            Add("climb-" + w, "Climbing", At((7 * w) + 4, 18), At((7 * w) + 4, 19, 30), "personal");
        }

        // tasks: open and done, one overdue
        void AddTask(string id, string title, int day, bool done, string calendar = "personal")
        {
            var start = monday.AddDays(day).ToDateTime(TimeOnly.MinValue);
            _events.Add(new Entry("task:" + id, (done ? "✓ " : "○ ") + title, start, start.AddDays(1), true, calendar, string.Empty, CalendarOccurrenceKind.Task));
        }

        AddTask("plotter", "Send A1 prints to the plotter", 2, false);
        AddTask("domain", "Renew domain example.org", 4, false);
        AddTask("train", "Book train to Haarlem", 1, true);
        AddTask("budget", "Review Q4 budget", 8, false, "work");
        AddTask("rent", "Pay rent", -2, false);
    }
}
#endif
