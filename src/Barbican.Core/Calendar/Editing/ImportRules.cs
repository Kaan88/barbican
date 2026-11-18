// Mirrors: Rust cal_extra.rs:430 sanitize_import, :467 parse_ics; Bulwark components/calendar/ical-import-modal.tsx, lib/calendar-event-normalization.ts; B-CAL-045. Contract from the M4 plan section 4.3; owner cal-edit replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Cleaning an event that came out of an .ics file before it is stored.</summary>
public static class ImportRules
{
    /// <summary>Drops id and unknown participant fields, sets calendarIds, turns replyTo/sendTo imip into organizerCalendarAddress and calendarAddress, makes a zero duration PT0S (P1D for an all-day event, which also loses its timeZone). Local alerts stay (a feed's do not: <c>FeedRules.SanitizeFeed</c>).</summary>
    public static JsonNode Sanitize(JsonNode source, string calendarId) => throw new PortPendingException("ImportRules.Sanitize");

    /// <summary>Whether the events carry alerts of their own (the import dialog then asks whether to keep them, G-004; the default is to drop them as Bulwark does when the user says no).</summary>
    public static bool HasAlerts(IReadOnlyList<JsonNode> events) => throw new PortPendingException("ImportRules.HasAlerts");
}
