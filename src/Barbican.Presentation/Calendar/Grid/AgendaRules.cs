// Mirrors: Rust cal_extra.rs:138 agenda_time, :229 agenda_task_title; Bulwark components/calendar/calendar-agenda-view.tsx; B-CAL-037, 047. Contract from the M4 plan section 4.5; owner cal-views replaces the bodies.

using Barbican.Core.Calendar;
using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Pure rules of the Agenda view.</summary>
public static class AgendaRules
{
    /// <summary>The title without the task marker the grid adds (the open circle U+25CB and the check U+2713), which the agenda draws itself.</summary>
    public static string TaskTitle(string title) => throw new PortPendingException("AgendaRules.TaskTitle");

    /// <summary>The time column for <paramref name="day"/>: "22:00 – 07:30" on the first day of an overnight event, "All day" on the days between, "until 07:30" on its last day; "All day" for an all-day event; 12-hour times when asked.</summary>
    public static string TimeLabel(CalendarOccurrence occurrence, DateOnly day, bool twelveHour) => throw new PortPendingException("AgendaRules.TimeLabel");
}
