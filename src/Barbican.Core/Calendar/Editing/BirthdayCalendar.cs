// Mirrors: Rust cal_extra.rs:84 birthday_parts, :99 birthdays_in, :125 birthday_occurrences, :128 birthday; Bulwark lib/birthday-calendar.ts; B-CAL-003/046 (fixed calendar id __birthday-calendar__, all-day events "cake Name (age)", ids bday:card:entry:year, 29 February on 28 February in common years).
// Contract from the M4 plan section 4.3; owner cal-edit replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Contact birthdays as agenda items.</summary>
public static class BirthdayCalendar
{
    public const string CalendarId = "__birthday-calendar__";

    /// <summary>(month, day, year or null) of a birth date: a PartialDate object, "2004-02-29", "--12-24" (no year) or a Timestamp.</summary>
    public static (int Month, int Day, int? Year)? PartsOf(JsonNode date) => throw new PortPendingException("BirthdayCalendar.PartsOf");

    /// <summary>The birthdays of the cards in [from, to], as all-day occurrences of kind Birthday: title with the age when the year is known ("cake Ann Lee (36)"), id "bday:c1:b:2026", calendar <see cref="CalendarId"/>.</summary>
    public static IReadOnlyList<CalendarOccurrence> In(IReadOnlyList<JsonNode> cards, DateOnly from, DateOnly to) => throw new PortPendingException("BirthdayCalendar.In");
}
