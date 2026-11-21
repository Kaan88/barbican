// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:436-444 locationsOf, :488-513 (the event doCalendarCreateEvent stores: status confirmed, busy, public, the plugin's uid in its own namespace),
// :520-560 (doCalendarUpdateEvent: only the named fields change; start, end, duration, timeZone and isAllDay are worked out together against the event's current times) and :410-412 pluginEventUid;
// assistant/c.calendar.ts and lib/calendar-description.ts plainDescriptionFields (a plugin writes plain text, also over an HTML description). JSCalendar: RFC 8984 4.2.1 (start, duration, timeZone),
// 4.2.3 (showWithoutTime), 4.2.5 (locations), 4.4.5 (participants), 4.3.2 (recurrenceRules).
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Recurrence;

namespace Barbican.Core.Plugins.Api.Calendar;

internal static class PluginEventJson
{
    /// <summary>pluginEventUid (:410-412): the uid, an at sign, the add-on id and <c>.plugin.bulwark</c>, so it cannot collide with events of other apps and the plugin finds its events again by uid. The suffix is kept as Bulwark has it so add-ons written for either client agree.</summary>
    internal static string PluginUid(string pluginId, string uid) => uid + "@" + pluginId + ".plugin.bulwark";

    /// <summary>locationsOf (:436-444): one named location, or null to remove them.</summary>
    internal static JsonNode? Locations(string location) => location.Length == 0 ? null : new JsonObject { ["loc1"] = new JsonObject { ["@type"] = "Location", ["name"] = location } };

    internal static JsonObject Create(PluginEventInput input, PluginTiming timing, string calendarId, string uid, JsonObject? participantFields)
    {
        var data = new JsonObject
        {
            ["@type"] = "Event", ["uid"] = uid, ["title"] = input.Title, ["start"] = timing.Start, ["duration"] = timing.Duration,
            ["timeZone"] = timing.TimeZone, ["showWithoutTime"] = timing.ShowWithoutTime, ["calendarIds"] = new JsonObject { [calendarId] = true },
            ["status"] = "confirmed", ["freeBusyStatus"] = "busy", ["privacy"] = "public",
        };
        if (input.Description is { Length: > 0 } description) data["description"] = description;
        if (input.Location is { Length: > 0 } location) data["locations"] = Locations(location);
        foreach (var (key, value) in participantFields ?? []) data[key] = value?.DeepClone();
        if (input.Repeat is not null) data["recurrenceRules"] = new JsonArray(input.Repeat.DeepClone());
        return data;
    }

    /// <summary>
    /// The patch of an update. <paramref name="timingBase"/> is the object whose current times the new ones are worked out against (the event, or one occurrence of it).
    /// False when the times cannot be worked out (:545-557 throwing).
    /// </summary>
    internal static bool TryChanges(PluginEventInput input, JsonNode existing, JsonNode timingBase, string defaultZone, JsonObject? participantFields, out JsonObject changes, out PluginTiming? timing)
    {
        changes = [];
        timing = null;
        if (input.Title is not null) changes["title"] = input.Title;
        if (input.Description is not null)
        {
            changes["description"] = input.Description;
            // plainDescriptionFields: an HTML description becomes the plain text the plugin wrote
            if (existing.Str("descriptionContentType") is { Length: > 0 } type && !string.Equals(type, "text/plain", StringComparison.OrdinalIgnoreCase)) changes["descriptionContentType"] = "text/plain";
        }

        if (input.Location is not null) changes["locations"] = Locations(input.Location);
        foreach (var (key, value) in participantFields ?? []) changes[key] = value?.DeepClone();
        if (input.HasRepeat) changes["recurrenceRules"] = input.Repeat is null ? null : new JsonArray(input.Repeat.DeepClone());
        if (!input.HasTiming) return true;
        var wasAllDay = timingBase.IsTrue("showWithoutTime");
        var isAllDay = input.Timing.IsAllDay ?? wasAllDay;
        var keepLength = input.Timing.End is null && input.Timing.Duration is null && isAllDay == wasAllDay;
        var stored = timingBase.Str("start") ?? "";
        var start = input.Timing.Start ?? (stored.Length >= (wasAllDay ? 10 : 19) ? stored[..(wasAllDay ? 10 : 19)] : null);
        var duration = input.Timing.Duration;
        if (keepLength && timingBase.Str("duration") is { } existingDuration && RecurrenceEngine.DurationSeconds(existingDuration) is > 0) duration = existingDuration;
        var zone = input.TimeZoneGiven ? input.Timing.TimeZone : isAllDay ? null : timingBase.Str("timeZone");
        if (!PluginTimingBuilder.TryBuild(new PluginTimingInput(start, input.Timing.End, duration, zone, isAllDay), defaultZone, out var built)) return false;
        changes["start"] = built.Start;
        changes["duration"] = built.Duration;
        changes["timeZone"] = built.TimeZone;
        changes["showWithoutTime"] = built.ShowWithoutTime;
        timing = built;
        return true;
    }
}
