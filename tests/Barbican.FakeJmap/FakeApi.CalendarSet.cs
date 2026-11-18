// The fake server's calendar writes (JMAP Calendars draft; Stalwart crates/jmap/src/calendar/set.rs, calendar_event/set.rs): Calendar/set (create, update, destroy, onSuccessSetIsDefault, onDestroyRemoveEvents) and CalendarEvent/set (create, update with JSON-pointer
// patches through FakeCalendars.ApplyPatch, destroy, sendSchedulingMessages recorded in FakeCalendars.SchedulingSent, notFound and invalidProperties errors, ifInState). Like Stalwart a pointer into a map the object does not have is an invalidPatch (the Rust app's
// "a pointer into a missing map fails"), and the server sets utcStart and utcEnd. Tests read the requests that arrived (SetRequests) and make the next write fail (FailNextSet). M4 plan section 4.7. Owner cal-edit.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private readonly List<(string Type, JsonObject Args)> _setRequests = [];
    private readonly Queue<string> _setFailures = new();

    /// <summary>Every Calendar/set and CalendarEvent/set that arrived, with its arguments (a copy).</summary>
    public IReadOnlyList<(string Type, JsonObject Args)> SetRequests
    {
        get
        {
            lock (_lock)
            {
                return [.. _setRequests];
            }
        }
    }

    /// <summary>The next calendar write is refused object by object with this SetError type (for instance "forbidden" or "serverFail"); nothing changes.</summary>
    public void FailNextSet(string errorType)
    {
        lock (_lock)
        {
            _setFailures.Enqueue(errorType);
        }
    }

    private sealed class SetRun(string type, JsonObject args, FakeCalendars cals, string? failure)
    {
        public string Type { get; } = type;

        public JsonObject Args { get; } = args;

        public FakeCalendars Cals { get; } = cals;

        public string? Failure { get; } = failure;

        public bool IsEvent { get; } = string.Equals(type, "CalendarEvent", StringComparison.Ordinal);

        public bool Notify { get; } = args["sendSchedulingMessages"] is JsonValue sv && sv.TryGetValue<bool>(out var b) && b;

        public JsonObject Created { get; } = [];

        public JsonObject Updated { get; } = [];

        public JsonArray Destroyed { get; } = [];

        public JsonObject NotCreated { get; } = [];

        public JsonObject NotUpdated { get; } = [];

        public JsonObject NotDestroyed { get; } = [];

        public string? DefaultTo { get; set; }
    }

    private (string? Name, JsonObject Args) CalendarSet(string type, JsonObject args)
    {
        if (options.Calendars is not { } cals || !options.CalendarsCapability)
        {
            return Error("unknownMethod");
        }

        string? failure;
        lock (_lock)
        {
            _setRequests.Add((type, (JsonObject)args.DeepClone()));
            failure = _setFailures.Count > 0 ? _setFailures.Dequeue() : null;
        }

        var oldState = cals.State(type);
        if ((string?)args["ifInState"] is { } expected && !string.Equals(expected, oldState, StringComparison.Ordinal))
        {
            return Error("stateMismatch");
        }

        var run = new SetRun(type, args, cals, failure);
        SetCreates(run);
        SetUpdates(run);
        SetDestroys(run);
        SetDefault(run);
        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["oldState"] = oldState,
            ["newState"] = cals.State(type),
            ["created"] = run.Created.Count == 0 ? null : run.Created,
            ["updated"] = run.Updated.Count == 0 ? null : run.Updated,
            ["destroyed"] = run.Destroyed.Count == 0 ? null : run.Destroyed,
            ["notCreated"] = run.NotCreated.Count == 0 ? null : run.NotCreated,
            ["notUpdated"] = run.NotUpdated.Count == 0 ? null : run.NotUpdated,
            ["notDestroyed"] = run.NotDestroyed.Count == 0 ? null : run.NotDestroyed,
        });
    }

    private static void SetCreates(SetRun run)
    {
        foreach (var (cid, raw) in run.Args["create"] as JsonObject ?? [])
        {
            if (run.Failure is not null)
            {
                run.NotCreated[cid] = SetError(run.Failure);
                continue;
            }

            var obj = raw is JsonObject o ? (JsonObject)o.DeepClone() : [];
            var bad = run.IsEvent ? InvalidEvent(obj) : InvalidCalendar(obj);
            if (bad is not null)
            {
                run.NotCreated[cid] = SetError("invalidProperties", bad);
                continue;
            }

            string id;
            if (run.IsEvent)
            {
                var calendarId = ((JsonObject)obj["calendarIds"]!).First().Key;
                obj["@type"] ??= "Event";
                obj["uid"] ??= Guid.NewGuid().ToString("N");
                SetUtc(obj);
                id = run.Cals.AddEvent(calendarId, obj);
                if (run.Notify && Takes(obj))
                {
                    run.Cals.SchedulingSent.Add(("create", id));
                }

                run.Created[cid] = new JsonObject { ["id"] = id, ["uid"] = (string?)obj["uid"], ["utcStart"] = obj["utcStart"]?.DeepClone(), ["utcEnd"] = obj["utcEnd"]?.DeepClone() };
            }
            else
            {
                id = run.Cals.AddCalendar((string)obj["name"]!, false, (string?)obj["color"] ?? "#3b82f6", obj);
                run.Created[cid] = new JsonObject { ["id"] = id };
            }

            if (string.Equals((string?)run.Args["onSuccessSetIsDefault"], "#" + cid, StringComparison.Ordinal))
            {
                run.DefaultTo = id;
            }
        }
    }

    private static void SetUpdates(SetRun run)
    {
        foreach (var (id, raw) in run.Args["update"] as JsonObject ?? [])
        {
            if (run.Failure is not null)
            {
                run.NotUpdated[id] = SetError(run.Failure);
                continue;
            }

            var patch = raw as JsonObject ?? [];
            string? error = null;
            var found = run.Cals.Update(run.Type, id, target =>
            {
                error = BadPatch(target, patch);
                if (error is not null)
                {
                    return;
                }

                foreach (var (path, value) in patch)
                {
                    FakeCalendars.ApplyPatch(target, path, value);
                }

                if (run.IsEvent)
                {
                    SetUtc(target);
                    if (run.Notify && Takes(target))
                    {
                        run.Cals.SchedulingSent.Add(("update", id));
                    }
                }
            });
            if (!found)
            {
                run.NotUpdated[id] = SetError("notFound");
            }
            else if (error is not null)
            {
                run.NotUpdated[id] = SetError("invalidPatch", error);
            }
            else
            {
                run.Updated[id] = null;
                if (string.Equals((string?)run.Args["onSuccessSetIsDefault"], id, StringComparison.Ordinal))
                {
                    run.DefaultTo = id;
                }
            }
        }
    }

    private static void SetDestroys(SetRun run)
    {
        var removeEvents = run.Args["onDestroyRemoveEvents"] is JsonValue rv && rv.TryGetValue<bool>(out var rb) && rb;
        foreach (var id in (run.Args["destroy"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>())
        {
            if (run.Failure is not null)
            {
                run.NotDestroyed[id] = SetError(run.Failure);
            }
            else if (run.IsEvent)
            {
                DestroyEvent(run, id);
            }
            else
            {
                DestroyCalendar(run, id, removeEvents);
            }
        }
    }

    private static void DestroyEvent(SetRun run, string id)
    {
        var existing = run.Cals.Events().FirstOrDefault(e => string.Equals((string?)e["id"], id, StringComparison.Ordinal));
        if (existing is null || !run.Cals.Destroy(run.Type, id))
        {
            run.NotDestroyed[id] = SetError("notFound");
            return;
        }

        if (run.Notify && Takes(existing))
        {
            run.Cals.SchedulingSent.Add(("destroy", id));
        }

        run.Destroyed.Add(id);
    }

    private static void DestroyCalendar(SetRun run, string id, bool removeEvents)
    {
        var calendar = run.Cals.Calendars().FirstOrDefault(c => string.Equals((string?)c["id"], id, StringComparison.Ordinal));
        if (calendar is null)
        {
            run.NotDestroyed[id] = SetError("notFound");
            return;
        }

        if ((bool?)calendar["isDefault"] == true)
        {
            run.NotDestroyed[id] = SetError("forbidden", "The default calendar cannot be deleted.");
            return;
        }

        var inside = run.Cals.Events().Where(e => (e["calendarIds"] as JsonObject)?.ContainsKey(id) == true).ToList();
        if (inside.Count > 0 && !removeEvents)
        {
            run.NotDestroyed[id] = SetError("calendarHasEvent");
            return;
        }

        foreach (var e in inside)
        {
            _ = run.Cals.Destroy("CalendarEvent", (string)e["id"]!);
        }

        _ = run.Cals.Destroy(run.Type, id);
        run.Destroyed.Add(id);
    }

    private static void SetDefault(SetRun run)
    {
        if (run.DefaultTo is null)
        {
            return;
        }

        foreach (var c in run.Cals.Calendars())
        {
            var cid = (string)c["id"]!;
            var want = string.Equals(cid, run.DefaultTo, StringComparison.Ordinal);
            if ((bool?)c["isDefault"] != want)
            {
                _ = run.Cals.Update("Calendar", cid, t => t["isDefault"] = want);
            }
        }
    }

    private static JsonObject SetError(string type, string? description = null) =>
        description is null
            ? new JsonObject { ["type"] = type }
            : new JsonObject { ["type"] = type, ["description"] = description, ["properties"] = string.Equals(type, "invalidProperties", StringComparison.Ordinal) ? new JsonArray(description) : null };

    /// <summary>The property that makes a new event invalid: calendarIds is required (Stalwart), and a start must be a LocalDateTime.</summary>
    private static string? InvalidEvent(JsonObject e)
    {
        if (e["calendarIds"] is not JsonObject { Count: > 0 })
        {
            return "calendarIds";
        }

        return e["start"] is JsonValue s && s.TryGetValue<string>(out var text) && !DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? "start" : null;
    }

    private static string? InvalidCalendar(JsonObject c) => c["name"] is JsonValue v && v.TryGetValue<string>(out var n) && n.Trim().Length > 0 ? null : "name";

    /// <summary>A pointer into a map or object the target lacks is an invalidPatch (a pointer into an array too, as Stalwart's patcher does for JSCalendar maps).</summary>
    private static string? BadPatch(JsonObject target, JsonObject patch)
    {
        foreach (var path in patch.Select(kv => kv.Key))
        {
            var parts = path.Split('/').Select(p => p.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
            JsonNode? node = target;
            foreach (var p in parts.Take(parts.Length - 1))
            {
                node = (node as JsonObject)?[p];
                if (node is not JsonObject)
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static bool Takes(JsonObject e) => e["participants"] is JsonObject { Count: > 0 };

    /// <summary>The instants Stalwart derives from start, timeZone and duration (UTC when the zone is unknown or missing).</summary>
    private static void SetUtc(JsonObject e)
    {
        if ((string?)e["start"] is not { } start || !DateTime.TryParseExact(start, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            return;
        }

        var offset = TimeSpan.Zero;
        if ((string?)e["timeZone"] is { Length: > 0 } zone)
        {
            try
            {
                offset = TimeZoneInfo.FindSystemTimeZoneById(zone).GetUtcOffset(wall);
            }
            catch (TimeZoneNotFoundException)
            {
                offset = TimeSpan.Zero;
            }
            catch (InvalidTimeZoneException)
            {
                offset = TimeSpan.Zero;
            }
        }

        var begin = new DateTimeOffset(wall, offset).UtcDateTime;
        e["utcStart"] = begin.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        e["utcEnd"] = begin.AddSeconds(DurationSeconds((string?)e["duration"])).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static long DurationSeconds(string? text)
    {
        if (text is null || !text.StartsWith('P'))
        {
            return 0;
        }

        long total = 0;
        var number = 0L;
        var inTime = false;
        foreach (var ch in text.AsSpan(1))
        {
            if (ch == 'T')
            {
                inTime = true;
            }
            else if (char.IsDigit(ch))
            {
                number = (number * 10) + (ch - '0');
            }
            else
            {
                total += ch switch { 'W' => number * 604800, 'D' => number * 86400, 'H' when inTime => number * 3600, 'M' when inTime => number * 60, 'S' when inTime => number, _ => 0 };
                number = 0;
            }
        }

        return total;
    }
}
