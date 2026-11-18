// Mirrors: Rust cal_editor.rs:90 event_organizer, :107 user_organizes, :112 attendees_of, :131 build_participant_map, :173 participant_fields, :192 asked_again, :238 form_rules, :252 build_data, :352 description_text,
// :366 OVERRIDE_KEYS, :370 occurrence_override, :391 override_patch, :403 truncate_patch, :428 following_series, :461 series_update; RFC 8984 4.1.1 recurrenceOverrides, 4.4.6 Participant, RFC 5546 (iTIP);
// Bulwark components/calendar/event-modal.tsx, lib/recurrence-scope-edit.ts, lib/recurrence-overrides.ts, lib/calendar-participants.ts; B-CAL-011..017.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Calendar.Reminders;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The pure event-editing rules: the data an editor saves, organiser detection, and the patches of "this occurrence", "this and following" and "all".</summary>
public static partial class EventEditor
{
    private static readonly string[] Privacy = ["public", "private", "secret"];

    /// <summary>The fields of a save. New event: title (trimmed), start, duration (PT1H30M; a wall-clock duration that follows DST: 01:00 to 04:00 on the day Berlin leaves DST is PT4H; an end before the start gives PT1H; all-day over three days is P3D),
    /// timeZone (null for all-day), status, uid (32 or more characters), useDefaultAlerts, no empty maps. Edit (<paramref name="ev"/> not null): participant and location ids and answers kept, unchanged fields left out, guests all removed clears participants
    /// and organizerCalendarAddress, a repeat removed clears recurrenceRules and recurrenceOverrides, a move by the organiser asks the guests again (needs-action). <paramref name="occurrence"/> is the occurrence being edited, for an edit of one.</summary>
    public static EventData BuildData(EventForm form, JsonNode? ev, (DateTime Start, DateTime End)? occurrence)
    {
        ArgumentNullException.ThrowIfNull(form);
        var d = new JsonObject { ["title"] = form.Title.Trim() };
        var start = form.AllDay ? form.Start.Date : form.Start;
        d["start"] = CalendarTime.FormatLocal(start);
        var duration = DurationOf(form, ev, start);
        d["duration"] = duration;
        d["timeZone"] = form.AllDay ? null : form.TimeZone;
        d["showWithoutTime"] = form.AllDay;
        d["calendarIds"] = new JsonObject { [form.Calendar] = true };
        StatusFields(d, form, ev);
        DescriptionField(d, form, ev);
        LocationFields(d, form, ev);
        var rules = FormRules(form);
        RepeatFields(d, rules, ev);
        AlertFields(d, form, ev);
        var changed = ev is not null && Changed(form, ev, occurrence, start, duration, rules);
        if (form.Organizer)
        {
            foreach (var (k, v) in ParticipantFields(ev, form.Attendees, form.Me, changed))
            {
                d[k] = v?.DeepClone();
            }
        }

        return new EventData(d, form.Organizer && form.Attendees.Count > 0 && form.Send);
    }

    /// <summary>The repeat rules the form gives, as a JSON array, or null for none.</summary>
    internal static JsonNode? FormRules(EventForm form) => form.Repeat switch
    {
        1 => new JsonArray(RecurrenceRule.Simple("daily").ToJson()),
        2 => new JsonArray(RecurrenceRule.Simple("weekly").ToJson()),
        3 => new JsonArray(RecurrenceRule.Simple("monthly").ToJson()),
        4 => new JsonArray(RecurrenceRule.Simple("yearly").ToJson()),
        5 => form.Custom is { } custom ? new JsonArray(custom.ToJson()) : null,
        _ => null,
    };

    private static string DurationOf(EventForm form, JsonNode? ev, DateTime start)
    {
        if (form.AllDay)
        {
            var last = form.End.Date < form.Start.Date ? form.Start.Date : form.End.Date;
            return $"P{(last - form.Start.Date).Days + 1}D";
        }

        var lastedNoTime = ev is not null && !ev.IsTrue("showWithoutTime") && CalendarTime.DurationSeconds(ev.Str("duration") ?? "PT1H") == 0;
        var end = form.End;
        if (end < start || (end == start && !lastedNoTime))
        {
            end = start.AddHours(1);
        }

        var ms = TimeZones.ZoneToUtcMs(end, form.TimeZone) - TimeZones.ZoneToUtcMs(start, form.TimeZone);
        return CalendarTime.DurationString(Math.Max(0, ms) / 1000);
    }

    private static void StatusFields(JsonObject d, EventForm form, JsonNode? ev)
    {
        var freeBusy = form.FreeBusy == 1 ? "free" : "busy";
        var privacy = form.Privacy is { } p ? Privacy[Math.Clamp(p, 0, 2)] : null;
        if (ev is null)
        {
            d["status"] = "confirmed";
            d["freeBusyStatus"] = freeBusy;
            d["privacy"] = privacy ?? "public";
            d["uid"] = NewUid();
            return;
        }

        if (!string.Equals(freeBusy, ev.Str("freeBusyStatus") ?? "busy", StringComparison.Ordinal))
        {
            d["freeBusyStatus"] = freeBusy;
        }

        if (privacy is not null && !string.Equals(privacy, ev.Str("privacy") ?? "public", StringComparison.Ordinal))
        {
            d["privacy"] = privacy;
        }
    }

    private static void DescriptionField(JsonObject d, EventForm form, JsonNode? ev)
    {
        var desc = form.Description.Trim();
        if (ev is null)
        {
            if (desc.Length > 0)
            {
                d["description"] = desc;
            }

            return;
        }

        if (!string.Equals(desc, DescriptionText(ev).Trim(), StringComparison.Ordinal))
        {
            d["description"] = desc;
            if (ev.IsString("descriptionContentType"))
            {
                d["descriptionContentType"] = "text/plain";
            }
        }
    }

    /// <summary>The event's place as the editor shows it: the first location's name, else the first link.</summary>
    internal static string LocationOf(JsonNode ev)
    {
        var name = ev.Obj("locations")?.Select(kv => kv.Value.Str("name")).FirstOrDefault(n => !string.IsNullOrEmpty(n));
        return name ?? ev.Obj("virtualLocations")?.Select(kv => kv.Value.Str("uri")).FirstOrDefault(u => u is not null) ?? "";
    }

    private static void LocationFields(JsonObject d, EventForm form, JsonNode? ev)
    {
        var loc = form.Location.Trim();
        var hadLoc = ev.Obj("locations") is { Count: > 0 };
        var hadVirtual = ev.Obj("virtualLocations") is { Count: > 0 };
        if (ev is not null && string.Equals(LocationOf(ev), loc, StringComparison.Ordinal))
        {
            return;
        }

        if (loc.StartsWith("http://", StringComparison.Ordinal) || loc.StartsWith("https://", StringComparison.Ordinal))
        {
            d["virtualLocations"] = new JsonObject { ["vl1"] = new JsonObject { ["@type"] = "VirtualLocation", ["uri"] = loc } };
            if (hadLoc)
            {
                d["locations"] = null;
            }
        }
        else if (loc.Length > 0)
        {
            // the place keeps its id (and any coordinates) when it had one
            var first = ev.Obj("locations")?.FirstOrDefault();
            var id = first?.Key ?? "l1";
            var place = first?.Value is JsonObject existing ? (JsonObject)existing.DeepClone() : new JsonObject { ["@type"] = "Location" };
            place["name"] = loc;
            d["locations"] = new JsonObject { [id] = place };
            if (hadVirtual)
            {
                d["virtualLocations"] = null;
            }
        }
        else
        {
            if (hadLoc)
            {
                d["locations"] = null;
            }

            if (hadVirtual)
            {
                d["virtualLocations"] = null;
            }
        }
    }

    private static void RepeatFields(JsonObject d, JsonNode? rules, JsonNode? ev)
    {
        if (rules is not null)
        {
            d["recurrenceRules"] = rules.DeepClone();
        }
        else if (ev.Arr("recurrenceRules") is { Count: > 0 })
        {
            d["recurrenceRules"] = null;
            if (ev.Child("recurrenceOverrides") is JsonObject)
            {
                d["recurrenceOverrides"] = null;
            }

            if (ev.Child("excludedRecurrenceRules") is JsonArray)
            {
                d["excludedRecurrenceRules"] = null;
            }
        }
    }

    private static void AlertFields(JsonObject d, EventForm form, JsonNode? ev)
    {
        if (form.UseDefaultAlerts)
        {
            d["useDefaultAlerts"] = true;
            if (ev is not null)
            {
                d["alerts"] = form.DefaultAlerts?.DeepClone();
            }

            return;
        }

        foreach (var (k, v) in ReminderRules.BuildAlertsPatch(ev, form.Rows, form.Kept, form.ReservedAlertIds))
        {
            d[k] = v?.DeepClone();
        }
    }

    /// <summary>Whether the save moves or reshapes the event, which makes the organiser ask the guests again.</summary>
    private static bool Changed(EventForm form, JsonNode ev, (DateTime Start, DateTime End)? occurrence, DateTime start, string duration, JsonNode? rules)
    {
        var allDayChanged = ev.IsTrue("showWithoutTime") != form.AllDay;
        bool moved;
        if (occurrence is { } occ)
        {
            var newDuration = CalendarTime.DurationSeconds(duration) ?? 0;
            moved = occ.Start != start || (long)(occ.End - occ.Start).TotalSeconds != newDuration || allDayChanged;
        }
        else
        {
            moved = CalendarTime.ParseLocal(ev.Str("start")) != start || !string.Equals(ev.Str("duration"), duration, StringComparison.Ordinal) || allDayChanged;
        }

        var tzChanged = !form.AllDay && !string.Equals(ev.Str("timeZone"), form.TimeZone, StringComparison.Ordinal);
        var ruleChanged = !RecurrenceEngine.SameRules(ev.Child("recurrenceRules") ?? new JsonArray(), rules ?? new JsonArray());
        return moved || tzChanged || ruleChanged;
    }

    /// <summary>The description as plain text (HTML descriptions are converted).</summary>
    public static string DescriptionText(JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var d = ev.Str("description") ?? "";
        var html = (ev.Str("descriptionContentType") ?? "").Contains("html", StringComparison.Ordinal) || (d.Contains("</", StringComparison.Ordinal) && d.Contains('>', StringComparison.Ordinal));
        if (!html)
        {
            return d;
        }

        var text = new System.Text.StringBuilder();
        var tag = false;
        foreach (var ch in d.Replace("<br>", "\n", StringComparison.Ordinal).Replace("<br/>", "\n", StringComparison.Ordinal).Replace("</p>", "\n", StringComparison.Ordinal))
        {
            switch (ch)
            {
                case '<': tag = true; break;
                case '>': tag = false; break;
                default:
                    if (!tag)
                    {
                        text.Append(ch);
                    }

                    break;
            }
        }

        return text.ToString().Replace("&nbsp;", " ", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal).Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal).Trim();
    }

    /// <summary>A fresh unique id for events, participants and alerts.</summary>
    internal static string NewUid() => Guid.NewGuid().ToString("D");
}
