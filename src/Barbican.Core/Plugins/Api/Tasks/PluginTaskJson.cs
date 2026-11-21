// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:785-889 (wholeMap, createMaps, sameAlert, countAfter, updateMaps, ownAlerts) and :975-1030 (dueOf, the task doTasksCreate stores, doTasksUpdate's patch).
// A map change is written entry by entry as JSON-pointer patches (RFC 6901, JMAP RFC 8620 5.3), so entries the plugin did not name, and members changed meanwhile elsewhere, stay as the server holds them; a map the
// task does not have yet is written whole. An alert set with the trigger and action it has keeps its `acknowledged`; a new trigger (a snooze) makes it fire again. While the task uses its calendar's default alerts
// (Stalwart shows them in `alerts` under the defaults' ids and drops on save every alert under one of those ids) the defaults the plugin keeps become the task's own under new ids, as the task form does, and
// an alert of the plugin's under a default's id is refused rather than lost. Deviation: due stays a JSCalendar LocalDateTime ("2026-05-01T00:00:00" for a day) like the Tasks view writes it, not Bulwark's bare date.
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Recurrence;

namespace Barbican.Core.Plugins.Api.Tasks;

internal static class PluginTaskJson
{
    private static readonly string[] Maps = ["alerts", "links", "relatedTo", "keywords"];

    /// <summary>dueOf (:976-983): a day (shown without a time) or a wall-clock time in the user's zone; none clears it.</summary>
    internal static void AddDue(JsonObject data, PluginTaskInput input, string defaultZone)
    {
        if (!input.HasDue) return;
        if (input.Due is not { } due)
        {
            data["due"] = null;
            data["showWithoutTime"] = true;
            return;
        }

        data["due"] = due.Date + "T" + (due.Time ?? "00:00:00");
        data["showWithoutTime"] = due.Time is null;
        if (due.Time is not null) data["timeZone"] = defaultZone;
    }

    /// <summary>The task doTasksCreate stores (:986-1004); the calendar is the writer's argument.</summary>
    internal static JsonObject Create(PluginTaskInput input, string defaultZone)
    {
        var task = new JsonObject
        {
            ["@type"] = "Task", ["title"] = input.Title, ["description"] = input.Description ?? "", ["priority"] = input.Priority ?? 0, ["progress"] = "needs-action",
        };
        if (input.HasDue) AddDue(task, input, defaultZone);
        else
        {
            task["showWithoutTime"] = true;
        }

        var changes = new[] { ("alerts", input.Alerts), ("links", input.Links), ("relatedTo", input.RelatedTo), ("keywords", input.Keywords) };
        foreach (var (name, change) in changes)
        {
            if (change is null) continue;
            var whole = Whole(change);
            if (whole is not null) task[name] = whole;
            // alerts turn the calendar's defaults off
            if (string.Equals(name, "alerts", StringComparison.Ordinal) && whole is not null) task["useDefaultAlerts"] = false;
        }

        return task;
    }

    /// <summary>updateMaps and the plain fields (:1006-1028): false when a map would hold too many entries, or an alert is given a default's id.</summary>
    internal static bool TryPatch(PluginTaskInput input, JsonNode existing, string defaultZone, out JsonObject patch)
    {
        patch = [];
        if (input.Title is not null) patch["title"] = input.Title;
        if (input.Description is not null) patch["description"] = input.Description;
        AddDue(patch, input, defaultZone);
        if (input.Priority is { } priority) patch["priority"] = priority;
        if (input.CalendarId is not null) patch["calendarIds"] = new JsonObject { [input.CalendarId] = true };
        var changes = new[] { input.Alerts, input.Links, input.RelatedTo, input.Keywords };
        var limits = new[] { PluginTaskMapReader.MaxAlerts, PluginTaskMapReader.MaxLinks, PluginTaskMapReader.MaxRelations, PluginTaskMapReader.MaxKeywords };
        for (var i = 0; i < Maps.Length; i++)
        {
            if (changes[i] is not { } change) continue;
            var name = Maps[i];
            if (change.ClearAll)
            {
                patch[name] = null;
                // else the calendar's defaults would still show and fire
                if (string.Equals(name, "alerts", StringComparison.Ordinal) && existing.IsTrue("useDefaultAlerts")) patch["useDefaultAlerts"] = false;
                continue;
            }

            var held = existing.Obj(name);
            var adds = change.Entries.Any(e => e.Value is not null && held?.ContainsKey(e.Key) != true);
            if (adds && CountAfter(held, change) > limits[i]) return false;
            if (string.Equals(name, "alerts", StringComparison.Ordinal) && existing.IsTrue("useDefaultAlerts"))
            {
                if (!TryOwnAlerts(existing, change, patch)) return false;
                continue;
            }

            if (held is null or { Count: 0 })
            {
                patch[name] = Whole(change);
                continue;
            }

            foreach (var (key, value) in change.Entries)
            {
                if (string.Equals(name, "alerts", StringComparison.Ordinal) && value is not null && held.TryGetPropertyValue(key, out var current) && SameAlert(current, value)) continue;
                patch[Pointer(name, key)] = value?.DeepClone();
            }
        }

        return true;
    }

    /// <summary>The patch that dismisses one alert (:1049-1050).</summary>
    internal static string AcknowledgedPath(string alertId) => Pointer("alerts", alertId) + "/acknowledged";

    private static JsonObject? Whole(PluginTaskMapChange change)
    {
        var set = change.Entries.Where(e => e.Value is not null).ToList();
        if (set.Count == 0) return null;
        var map = new JsonObject();
        foreach (var (key, value) in set) map[key] = value!.DeepClone();
        return map;
    }

    private static int CountAfter(JsonObject? held, PluginTaskMapChange change)
    {
        var keys = new HashSet<string>(held?.Select(p => p.Key) ?? [], StringComparer.Ordinal);
        foreach (var (key, value) in change.Entries)
        {
            if (value is null) _ = keys.Remove(key);
            else _ = keys.Add(key);
        }

        return keys.Count;
    }

    private static string Pointer(string name, string key) => name + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>sameAlert (:802-807): the same action and the same trigger (an instant, or an offset from the same end).</summary>
    private static bool SameAlert(JsonNode? a, JsonNode? b) => string.Equals(a.Str("action") ?? "display", b.Str("action") ?? "display", StringComparison.Ordinal)
        && string.Equals(TriggerKey(a.Child("trigger")), TriggerKey(b.Child("trigger")), StringComparison.Ordinal);

    private static string TriggerKey(JsonNode? trigger) => string.Equals(trigger.Str("@type"), "AbsoluteTrigger", StringComparison.Ordinal)
        ? "at " + (DateTimeOffset.TryParse(trigger.Str("when"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when.ToUnixTimeMilliseconds() : -1)
        : "offset " + RecurrenceEngine.DurationSeconds(trigger.Str("offset") ?? "PT0S") + " " + (string.Equals(trigger.Str("relativeTo"), "end", StringComparison.Ordinal) ? "end" : "start");

    /// <summary>ownAlerts (:872-889): the defaults the plugin keeps become the task's own under new ids.</summary>
    private static bool TryOwnAlerts(JsonNode existing, PluginTaskMapChange change, JsonObject patch)
    {
        var defaults = existing.Obj("alerts") ?? [];
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var set = new List<(string Key, JsonNode Value)>();
        foreach (var (key, value) in change.Entries)
        {
            if (value is null) _ = removed.Add(key);
            else if (defaults.ContainsKey(key)) return false;
            else set.Add((key, value));
        }

        var taken = new HashSet<string>(defaults.Select(d => d.Key).Concat(set.Select(s => s.Key)), StringComparer.Ordinal);
        var alerts = new JsonObject();
        var next = 1;
        foreach (var (key, alert) in defaults)
        {
            if (removed.Contains(key) || alert is null) continue;
            string id;
            do id = "own" + next++.ToString(CultureInfo.InvariantCulture);
            while (!taken.Add(id));
            alerts[id] = alert.DeepClone();
        }

        foreach (var (key, value) in set) alerts[key] = value.DeepClone();
        patch["useDefaultAlerts"] = false;
        patch["alerts"] = alerts.Count > 0 ? alerts : null;
        return true;
    }
}
