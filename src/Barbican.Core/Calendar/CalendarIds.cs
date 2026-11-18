// Mirrors: Rust calendar.rs:226 ns, :231 split_ns, :236 server_ref, :242 server_calendar_ids, :248 base_id, :251 rid_of, :341 namespace_event; B-CAL-001. Another account's ids are prefixed "<account>~" so ids from several
// accounts never collide. Contract from the M4 plan section 4.2; owner cal-data replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar;

/// <summary>Namespaced calendar and event ids.</summary>
public static class CalendarIds
{
    /// <summary>The primary account's ids stay plain; a shared account's are "x~id".</summary>
    public static string Ns(string primaryAccount, string account, string id) => throw new PortPendingException("CalendarIds.Ns");

    public static (string? Account, string Id) SplitNs(string id) => throw new PortPendingException("CalendarIds.SplitNs");

    /// <summary>"e|2026-01-01T00:00:00" is "e".</summary>
    public static string BaseId(string id) => throw new PortPendingException("CalendarIds.BaseId");

    /// <summary>"e|2026-01-01T00:00:00" is "2026-01-01T00:00:00"; null for a plain id.</summary>
    public static string? RecurrenceIdOf(string id) => throw new PortPendingException("CalendarIds.RecurrenceIdOf");

    /// <summary>Gives an event of another account namespaced ids (its own and its calendars').</summary>
    public static void NamespaceEvent(JsonNode ev, string primaryAccount, string account) => throw new PortPendingException("CalendarIds.NamespaceEvent");

    /// <summary>The calendarIds map with the namespace removed, for the server.</summary>
    public static JsonNode ServerCalendarIds(JsonNode calendarIds) => throw new PortPendingException("CalendarIds.ServerCalendarIds");
}
