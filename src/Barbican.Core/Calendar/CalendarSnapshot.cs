// Mirrors: Rust calendar.rs:273 load (Calendar/get with the 11 properties, CalendarEvent/query + get_all in maxObjectsInGet chunks per account), :266 EVENT_PROPS (34 properties), :254 calendar_accounts. Contract from the M4 plan section 4.2; owner cal-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar;

/// <summary>Everything the calendar knows of one login: its calendars and events (own and shared accounts, ids namespaced by <see cref="CalendarIds"/>), as JSCalendar objects, and the state strings for /changes.</summary>
public sealed record CalendarSnapshot(string AccountKey, IReadOnlyList<JsonNode> Calendars, IReadOnlyList<JsonNode> Events, string? CalendarState, string? EventState);
