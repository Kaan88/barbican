// Mirrors: Rust calendar.rs:981 ics_escape, :986 ics_fold, :999 ics_local, :1002 event_ics; Bulwark lib/calendar-ics-export.ts; RFC 5545 3.1 (folding), 3.3.11 (escaping). I-328 "Export as .ics". Contract from the M4 plan section 4.3; owner cal-edit replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>One event as an .ics file.</summary>
public static class IcsWriter
{
    /// <summary>(file name, text) of a VCALENDAR with one VEVENT: UID, DTSTAMP, SUMMARY, DTSTART and DTEND (TZID or floating, VALUE=DATE for all-day), LOCATION, DESCRIPTION, RRULE, lines folded at 75 octets, CRLF.</summary>
    public static (string FileName, string Text) Event(JsonNode ev) => throw new PortPendingException("IcsWriter.Event");
}
