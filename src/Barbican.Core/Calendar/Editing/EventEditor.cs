// Mirrors: Rust cal_editor.rs:90 event_organizer, :107 user_organizes, :112 attendees_of, :131 build_participant_map, :173 participant_fields, :192 asked_again, :238 form_rules, :252 build_data, :352 description_text,
// :366 OVERRIDE_KEYS, :370 occurrence_override, :391 override_patch, :403 truncate_patch, :428 following_series, :461 series_update; RFC 8984 4.1.1 recurrenceOverrides, 4.4.6 Participant, RFC 5546 (iTIP);
// Bulwark components/calendar/event-modal.tsx, lib/recurrence-scope-edit.ts, lib/recurrence-overrides.ts, lib/calendar-participants.ts; B-CAL-011..017. Contract from the M4 plan section 4.3; owner cal-edit replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The pure event-editing rules: the data an editor saves, organiser detection, and the patches of "this occurrence", "this and following" and "all".</summary>
public static class EventEditor
{
    /// <summary>The fields of a save. New event: title (trimmed), start, duration (PT1H30M; a wall-clock duration that follows DST: 01:00 to 04:00 on the day Berlin leaves DST is PT4H; an end before the start gives PT1H; all-day over three days is P3D),
    /// timeZone (null for all-day), status, uid (32 or more characters), useDefaultAlerts, no empty maps. Edit (<paramref name="ev"/> not null): participant and location ids and answers kept, unchanged fields left out, guests all removed clears participants
    /// and organizerCalendarAddress, a repeat removed clears recurrenceRules and recurrenceOverrides, a move by the organiser asks the guests again (needs-action). <paramref name="occurrence"/> is the occurrence being edited, for an edit of one.</summary>
    public static EventData BuildData(EventForm form, JsonNode? ev, (DateTime Start, DateTime End)? occurrence) => throw new PortPendingException("EventEditor.BuildData");

    /// <summary>The user organises: no organiser at all, an owner participant or organizerCalendarAddress among <paramref name="me"/> (case-insensitive), or isOrigin.</summary>
    public static bool UserOrganizes(JsonNode ev, IReadOnlyList<string> me) => throw new PortPendingException("EventEditor.UserOrganizes");

    /// <summary>The owner participant or organizerCalendarAddress as a person.</summary>
    public static CalendarPerson? EventOrganizer(JsonNode ev) => throw new PortPendingException("EventEditor.EventOrganizer");

    /// <summary>The non-owner participants, deduplicated case-insensitively.</summary>
    public static IReadOnlyList<CalendarPerson> AttendeesOf(JsonNode ev) => throw new PortPendingException("EventEditor.AttendeesOf");

    /// <summary>The participants map with every guest but the organiser and the user set back to needs-action.</summary>
    public static JsonNode AskedAgain(JsonNode participants, IReadOnlyList<string> me) => throw new PortPendingException("EventEditor.AskedAgain");

    /// <summary>The description as plain text (HTML descriptions are converted).</summary>
    public static string DescriptionText(JsonNode ev) => throw new PortPendingException("EventEditor.DescriptionText");

    /// <summary>The override of one occurrence: only the keys an override may carry (title, description, locations, virtualLocations, freeBusyStatus, participants, alerts) plus a start when it moved; a duration only when it differs.</summary>
    public static JsonObject OccurrenceOverride(JsonNode master, string recurrenceId, JsonObject data, DateTime newStart) => throw new PortPendingException("EventEditor.OccurrenceOverride");

    /// <summary>The patch that stores the override: <c>recurrenceOverrides</c> as a whole object when the master has none, else the pointer <c>recurrenceOverrides/&lt;rid&gt;</c>.</summary>
    public static JsonObject OverridePatch(JsonNode master, string recurrenceId, JsonNode value) => throw new PortPendingException("EventEditor.OverridePatch");

    /// <summary>"This and following", step 1: the master's rule ends a second before <paramref name="recurrenceId"/> (until, no count), overrides from there on are removed; and the patch that restores the master if step 2 fails.</summary>
    public static (JsonObject Truncate, JsonObject Restore) TruncatePatch(JsonNode master, string recurrenceId) => throw new PortPendingException("EventEditor.TruncatePatch");

    /// <summary>"This and following", step 2: the new series (new uid, start at the recurrence id, COUNT left after the split by <c>OccurrencesBefore</c>, guests asked again, the edited fields).</summary>
    public static JsonObject FollowingSeries(JsonNode master, string recurrenceId, JsonObject data, IReadOnlyList<string> me) => throw new PortPendingException("EventEditor.FollowingSeries");

    /// <summary>"All": the series moves as far as the edited occurrence did; a repeat the user did not touch stays out of the patch.</summary>
    public static JsonObject SeriesUpdate(JsonNode master, DateTime occurrenceStart, JsonObject data) => throw new PortPendingException("EventEditor.SeriesUpdate");
}
