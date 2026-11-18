// Mirrors: Rust cal_extra.rs CxAgendaRow (header, today, id, title, time, location, colour, kind, done, empty). Owner cal-views.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>One row of the Agenda: the day heading is on the first row of a day only; an empty row is "nothing today".</summary>
public sealed record AgendaRow(string Header, DateOnly Day, bool Today, bool Empty, string? Id, string Title, string Time, string Location, string CalendarId, CalendarOccurrenceKind Kind, bool Done);
