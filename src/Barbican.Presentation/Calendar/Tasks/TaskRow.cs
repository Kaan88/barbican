// Mirrors: Rust cal_extra.rs CxTask (the row of the Tasks view); Bulwark components/calendar/task-list-view.tsx. Owner cal-ui.

namespace Barbican.Presentation.Calendar.Tasks;

/// <summary>One row of the Tasks view or the sidebar list. Immutable: a change of the store builds new rows.</summary>
/// <param name="Id">The task id.</param>
/// <param name="Title">The title, or "(untitled task)".</param>
/// <param name="Notes">The first line of the notes.</param>
/// <param name="Due">"Today", "Tomorrow", "Overdue · Nov 3", "Nov 20 14:30"; empty without a due date.</param>
/// <param name="DueRank">0 dim, 1 today, 2 overdue.</param>
/// <param name="Done">Completed (cancelled tasks are finished but not ticked).</param>
/// <param name="Finished">Completed or cancelled.</param>
/// <param name="Priority">0 none, 1 high, 2 medium, 3 low.</param>
/// <param name="ListId">The task's calendar.</param>
/// <param name="ListName">The calendar's name.</param>
/// <param name="Color">The calendar's colour.</param>
/// <param name="DueDay">The due date, for the day filter.</param>
public sealed record TaskRow(string Id, string Title, string Notes, string Due, int DueRank, bool Done, bool Finished, int Priority, string ListId, string ListName, string Color, DateOnly? DueDay);
