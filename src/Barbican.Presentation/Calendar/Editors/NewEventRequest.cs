// Mirrors: Rust cal_editor.rs:512 open_new (date, hour, minute, all-day, title), :1431 quick; Bulwark components/calendar/quick-create-popover.tsx, event-modal.tsx. Contract from the M4 plan section 4.6; owner cal-ui.

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>What the views ask the editors to open for a new event: the range clicked or dragged, on the user's clock, and an optional calendar and title (the month cell's quick create, "New event in this calendar").</summary>
public sealed record NewEventRequest(DateTime Start, DateTime End, bool AllDay, string? CalendarId = null, string Title = "");
