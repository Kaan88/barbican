// Mirrors: Rust cal_editor.rs:366 OVERRIDE_KEYS, :370 occurrence_override, :391 override_patch, :403 truncate_patch, :428 following_series, :461 series_update; Bulwark lib/recurrence-scope-edit.ts (seriesUpdatesFromOccurrence, truncateSeries,
// followingSeriesData), lib/recurrence-overrides.ts; RFC 8984 4.3.3 (recurrenceOverrides keyed by the recurrence id).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Recurrence;

namespace Barbican.Core.Calendar.Editing;

public static partial class EventEditor
{
    private static readonly string[] OverrideKeys = ["title", "description", "locations", "virtualLocations", "freeBusyStatus", "participants", "alerts"];

    private static readonly string[] FollowingKeep =
    [
        "title", "description", "descriptionContentType", "locations", "virtualLocations", "links", "keywords", "categories", "color",
        "locale", "status", "freeBusyStatus", "privacy", "calendarIds", "useDefaultAlerts", "duration", "timeZone", "showWithoutTime",
    ];

    private static string Pointer(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>The override of one occurrence: only the keys an override may carry (title, description, locations, virtualLocations, freeBusyStatus, participants, alerts) plus a start when it moved; a duration only when it differs.</summary>
    public static JsonObject OccurrenceOverride(JsonNode master, string recurrenceId, JsonObject data, DateTime newStart)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(recurrenceId);
        ArgumentNullException.ThrowIfNull(data);
        var masterObject = master as JsonObject;
        var o = master.Obj("recurrenceOverrides")?[recurrenceId] is JsonObject had ? (JsonObject)had.DeepClone() : [];
        foreach (var key in OverrideKeys)
        {
            if (!data.TryGetPropertyValue(key, out var value))
            {
                continue;
            }

            var masterHas = masterObject?.ContainsKey(key) == true;
            var masterValue = masterHas ? masterObject![key] : null;
            var equal = masterHas && JsonNode.DeepEquals(masterValue, value);
            if (!equal && !(value is null && masterValue is null))
            {
                o[key] = value?.DeepClone();
            }
        }

        if (newStart != CalendarTime.ParseLocal(recurrenceId))
        {
            o["start"] = CalendarTime.FormatLocal(newStart);
        }
        else
        {
            o.Remove("start");
        }

        if (data.ContainsKey("duration"))
        {
            if (!JsonNode.DeepEquals(master.Child("duration"), data["duration"]))
            {
                o["duration"] = data["duration"]?.DeepClone();
            }
            else
            {
                o.Remove("duration");
            }
        }

        if (data.ContainsKey("showWithoutTime") && master.IsTrue("showWithoutTime") != (data["showWithoutTime"] is JsonValue sw && sw.TryGetValue<bool>(out var b) && b))
        {
            o["showWithoutTime"] = data["showWithoutTime"]?.DeepClone();
        }

        o.Remove("excluded");
        return o;
    }

    /// <summary>The patch that stores the override: <c>recurrenceOverrides</c> as a whole object when the master has none, else the pointer <c>recurrenceOverrides/&lt;rid&gt;</c> (a pointer into a missing map fails on the server).</summary>
    public static JsonObject OverridePatch(JsonNode master, string recurrenceId, JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(recurrenceId);
        ArgumentNullException.ThrowIfNull(value);
        return master.Obj("recurrenceOverrides") is { Count: > 0 }
            ? new JsonObject { [$"recurrenceOverrides/{Pointer(recurrenceId)}"] = value.DeepClone() }
            : new JsonObject { ["recurrenceOverrides"] = new JsonObject { [recurrenceId] = value.DeepClone() } };
    }

    /// <summary>"This and following", step 1: the master's rule ends a second before <paramref name="recurrenceId"/> (until, no count), overrides from there on are removed; and the patch that restores the master if step 2 fails.</summary>
    public static (JsonObject Truncate, JsonObject Restore) TruncatePatch(JsonNode master, string recurrenceId)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(recurrenceId);
        var split = CalendarTime.ParseLocal(recurrenceId) ?? default;
        var until = CalendarTime.FormatLocal(split.AddSeconds(-1));
        var rules = new JsonArray();
        foreach (var r in master.Arr("recurrenceRules") ?? [])
        {
            if (r?.DeepClone() is JsonObject rule)
            {
                rule["until"] = until;
                rule.Remove("count");
                rules.Add((JsonNode)rule);
            }
        }

        var patch = new JsonObject { ["recurrenceRules"] = rules };
        var restore = new JsonObject { ["recurrenceRules"] = master.Child("recurrenceRules")?.DeepClone() };
        var removed = false;
        foreach (var (key, _) in master.Obj("recurrenceOverrides") ?? [])
        {
            if (CalendarTime.ParseLocal(key) is { } t && t >= split)
            {
                patch[$"recurrenceOverrides/{Pointer(key)}"] = null;
                removed = true;
            }
        }

        if (removed)
        {
            restore["recurrenceOverrides"] = master.Child("recurrenceOverrides")?.DeepClone();
        }

        return (patch, restore);
    }

    /// <summary>"This and following", step 2: the new series (new uid, start at the recurrence id, COUNT left after the split by <c>OccurrencesBefore</c>, guests asked again, the edited fields).</summary>
    public static JsonObject FollowingSeries(JsonNode master, string recurrenceId, JsonObject data, IReadOnlyList<string> me)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(recurrenceId);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(me);
        var n = FollowingBase(master);
        var ruleChanged = data.ContainsKey("recurrenceRules") && !RecurrenceEngine.SameRules(data["recurrenceRules"] ?? new JsonArray(), master.Child("recurrenceRules") ?? new JsonArray());
        foreach (var (k, v) in data)
        {
            if (k is "recurrenceRules" or "recurrenceOverrides" or "excludedRecurrenceRules")
            {
                continue;
            }

            if (v is null)
            {
                n.Remove(k);
            }
            else
            {
                n[k] = v.DeepClone();
            }
        }

        var rules = ruleChanged ? data["recurrenceRules"]?.DeepClone() : CountLeft(master, recurrenceId);
        if (rules is not null)
        {
            n["recurrenceRules"] = rules;
        }

        n["start"] ??= recurrenceId;
        n["uid"] = NewUid();
        n["@type"] = "Event";
        if (n["participants"] is { } p)
        {
            n["participants"] = AskedAgain(p, me);
        }

        return n;
    }

    private static JsonObject FollowingBase(JsonNode master)
    {
        var n = new JsonObject();
        foreach (var key in FollowingKeep)
        {
            if (master.Child(key) is { } v)
            {
                n[key] = v.DeepClone();
            }
        }

        if (!master.IsTrue("useDefaultAlerts") && master.Child("alerts") is JsonObject alerts)
        {
            n["alerts"] = alerts.DeepClone();
        }

        if (master.Child("participants") is JsonObject participants)
        {
            n["participants"] = participants.DeepClone();
        }

        if (master.IsString("organizerCalendarAddress"))
        {
            n["organizerCalendarAddress"] = master.Str("organizerCalendarAddress");
        }

        return n;
    }

    /// <summary>The master's rules for the new series: what is left of each COUNT after the occurrences before the split.</summary>
    private static JsonArray CountLeft(JsonNode master, string recurrenceId)
    {
        var start = CalendarTime.ParseLocal(master.Str("start")) ?? default;
        var split = CalendarTime.ParseLocal(recurrenceId) ?? default;
        var rules = new JsonArray();
        foreach (var r in master.Arr("recurrenceRules") ?? [])
        {
            if (r?.DeepClone() is not JsonObject rule)
            {
                continue;
            }

            if (rule["count"] is JsonValue c && c.TryGetValue<long>(out var count))
            {
                rule["count"] = (int)Math.Clamp(count - RecurrenceEngine.OccurrencesBefore(start, RecurrenceRule.Parse(rule), split), 1, int.MaxValue);
            }

            rules.Add((JsonNode)rule);
        }

        return rules;
    }

    /// <summary>"All": the series moves as far as the edited occurrence did; a repeat the user did not touch stays out of the patch.</summary>
    public static JsonObject SeriesUpdate(JsonNode master, DateTime occurrenceStart, JsonObject data)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(data);
        var d = (JsonObject)data.DeepClone();
        var newStart = CalendarTime.ParseLocal(d.Str("start"));
        var masterStart = CalendarTime.ParseLocal(master.Str("start"));
        if (newStart is { } ns && masterStart is { } ms && ns != occurrenceStart)
        {
            d["start"] = CalendarTime.FormatLocal(ms + (ns - occurrenceStart));
        }
        else
        {
            d.Remove("start");
        }

        if (d.ContainsKey("recurrenceRules") && RecurrenceEngine.SameRules(d["recurrenceRules"] ?? new JsonArray(), master.Child("recurrenceRules") ?? new JsonArray()))
        {
            d.Remove("recurrenceRules");
        }

        return d;
    }
}
