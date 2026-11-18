// Mirrors: Rust cal_extra.rs:467 parse_ics (CalendarEvent/parse of an uploaded blob), :506 import_begin, :522 import_choose, :553 import_run; Bulwark components/calendar/ical-import-modal.tsx; plan: Ical.Net parses the file (VEVENT, VTODO), the server parse is the fallback.
// G-004: alerts in the file are kept only when the user says so. Contract from the M4 plan section 4.3; owner cal-edit.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>.ics files into calendars.</summary>
public interface IIcsImporter
{
    /// <summary>The events and tasks of the file as JSCalendar (Ical.Net, then the server's CalendarEvent/parse for what it cannot read); at most 5 MB.</summary>
    Task<Result<IReadOnlyList<JsonNode>>> ParseAsync(string accountKey, ReadOnlyMemory<byte> ics, CancellationToken ct);

    /// <summary>Stores the events in the calendar (<see cref="ImportRules.Sanitize"/>, alerts of the file dropped only when <paramref name="keepAlerts"/> is false (the dialog's default is true, as Bulwark keeps them: D-M4-09), no scheduling messages), in chunks; returns how many were stored, with the first failure if any.</summary>
    Task<Result<int>> ImportAsync(string accountKey, string calendarId, IReadOnlyList<JsonNode> events, bool keepAlerts, CancellationToken ct);
}
