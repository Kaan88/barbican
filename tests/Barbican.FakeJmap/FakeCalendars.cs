// The fake server's calendar data (JMAP Calendars draft: Calendar, CalendarEvent; Stalwart crates/jmap/src/calendar/, calendar_event/): calendars and events as JSCalendar objects, a change log per type with state strings for /changes, and the
// scheduling messages a CalendarEvent/set asked for (sendSchedulingMessages). A test hands one to FakeJmapOptions.Calendars; the arms are in FakeApi.Calendar.cs. M4 plan section 4.7. Owner cal-data (reads, changes); cal-edit adds to FakeApi.CalendarSet.cs.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>An in-memory calendar account.</summary>
public sealed class FakeCalendars
{
    private readonly Lock _lock = new();
    private readonly List<JsonObject> _calendars = [];
    private readonly List<JsonObject> _events = [];
    private readonly List<(int State, string Type, string Id, char Kind)> _log = [];
    private int _state = 1;
    private int _next;

    /// <summary>Every CalendarEvent/set that sent scheduling messages: (operation create, update or destroy, id).</summary>
    public IList<(string Operation, string Id)> SchedulingSent { get; } = [];

    /// <summary>Raised after each change with the changed type and its new state (like FakeStore.Changed); the push hub wiring of cal-data listens.</summary>
    public event EventHandler<FakeStateChangedEventArgs>? Changed;

    /// <summary>Adds a calendar; returns its id.</summary>
    public string AddCalendar(string name, bool isDefault = false, string color = "#3b82f6", JsonObject? extra = null)
    {
        lock (_lock)
        {
            var id = "cal" + (++_next).ToString(CultureInfo.InvariantCulture);
            var c = new JsonObject { ["id"] = id, ["name"] = name, ["color"] = color, ["isDefault"] = isDefault, ["isVisible"] = true, ["sortOrder"] = _calendars.Count, ["isSubscribed"] = true, ["includeInAvailability"] = "all", ["myRights"] = new JsonObject { ["mayReadItems"] = true, ["mayWriteAll"] = true, ["mayDelete"] = true } };
            foreach (var (k, v) in extra ?? [])
            {
                c[k] = v?.DeepClone();
            }

            _calendars.Add(c);
            Log("Calendar", id, 'c');
            return id;
        }
    }

    /// <summary>Adds an event (a full JSCalendar object; id and calendarIds are completed); returns its id.</summary>
    public string AddEvent(string calendarId, JsonObject ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        lock (_lock)
        {
            var id = ev["id"] is JsonValue v ? (string)v! : "ev" + (++_next).ToString(CultureInfo.InvariantCulture);
            ev["id"] = id;
            ev["@type"] ??= "Event";
            ev["calendarIds"] ??= new JsonObject { [calendarId] = true };
            ev["uid"] ??= Guid.NewGuid().ToString("N");
            _events.Add(ev);
            Log("CalendarEvent", id, 'c');
            return id;
        }
    }

    public IReadOnlyList<JsonObject> Calendars()
    {
        lock (_lock)
        {
            return [.. _calendars.Select(c => (JsonObject)c.DeepClone())];
        }
    }

    public IReadOnlyList<JsonObject> Events()
    {
        lock (_lock)
        {
            return [.. _events.Select(e => (JsonObject)e.DeepClone())];
        }
    }

    public string State(string type)
    {
        lock (_lock)
        {
            return type + "-" + _log.Where(l => string.Equals(l.Type, type, StringComparison.Ordinal)).Select(l => l.State).DefaultIfEmpty(1).Max().ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The created, updated and destroyed ids since a state string of <see cref="State"/>; null when the state is unknown (cannotCalculateChanges).</summary>
    public (List<string> Created, List<string> Updated, List<string> Destroyed)? ChangesSince(string type, string since)
    {
        lock (_lock)
        {
            var prefix = type + "-";
            if (!since.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(since.AsSpan(prefix.Length), CultureInfo.InvariantCulture, out var s))
            {
                return null;
            }

            List<string> created = [];
            List<string> updated = [];
            List<string> destroyed = [];
            foreach (var l in _log.Where(l => l.State > s && string.Equals(l.Type, type, StringComparison.Ordinal)))
            {
                (l.Kind == 'c' ? created : l.Kind == 'u' ? updated : destroyed).Add(l.Id);
            }

            return (created, updated, destroyed);
        }
    }

    /// <summary>Runs <paramref name="change"/> on the calendar or event with this id under the lock and logs an update; false when there is none.</summary>
    internal bool Update(string type, string id, Action<JsonObject> change)
    {
        lock (_lock)
        {
            var target = (string.Equals(type, "Calendar", StringComparison.Ordinal) ? _calendars : _events).Find(o => string.Equals((string?)o["id"], id, StringComparison.Ordinal));
            if (target is null)
            {
                return false;
            }

            change(target);
            Log(type, id, 'u');
            return true;
        }
    }

    internal bool Destroy(string type, string id)
    {
        lock (_lock)
        {
            var list = string.Equals(type, "Calendar", StringComparison.Ordinal) ? _calendars : _events;
            var removed = list.RemoveAll(o => string.Equals((string?)o["id"], id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                Log(type, id, 'd');
            }

            return removed;
        }
    }

    private void Log(string type, string id, char kind)
    {
        _log.Add((++_state, type, id, kind));
        Changed?.Invoke(this, new FakeStateChangedEventArgs(new Dictionary<string, string>(StringComparer.Ordinal) { [type] = State(type) }));
    }

    /// <summary>Applies a JSON-pointer patch entry (RFC 8620 5.3: "a/b" sets a nested value, null removes it; "~1" is "/" and "~0" is "~").</summary>
    public static void ApplyPatch(JsonObject target, string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        var parts = path.Split('/').Select(p => p.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        var node = target;
        foreach (var p in parts.Take(parts.Length - 1))
        {
            node = node[p] as JsonObject ?? (JsonObject)(node[p] = new JsonObject())!;
        }

        if (value is null)
        {
            _ = node.Remove(parts[^1]);
        }
        else
        {
            node[parts[^1]] = value.DeepClone();
        }
    }
}
