// Mirrors: Rust cal_editor.rs:512 open_new, :554 open_existing, cal_extra.rs:315 open_task, :1115 scope_go; Bulwark components/calendar/event-modal.tsx, quick-create-popover.tsx, task-modal.tsx, recurrence-scope-dialog.tsx; I-300, I-314, I-322..I-324, I-333..I-339, I-342.
// Contract from the M4 plan section 4.6; owner cal-ui implements, cal-views (grid clicks, drag-to-create) and the context menus consume it.

using Barbican.Core.Calendar.Editing;

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>Opens the event and task editors, the quick-create popover and the scope question. They are in-window cards (ADR 0007); the plugin hooks <c>onCalendarEventFormOpen</c> and <c>onCalendarEventFormSave</c> belong to them.</summary>
public interface ICalendarEditors
{
    /// <summary>The quick-create popover (title, time, calendar) with "More options" opening the full editor.</summary>
    Task OpenQuickCreateAsync(NewEventRequest request, CancellationToken ct);

    /// <summary>The full event editor for a new event.</summary>
    Task OpenNewEventAsync(NewEventRequest request, CancellationToken ct);

    /// <summary>The editor for an existing event or occurrence (<c>occurrenceId</c> as in <c>CalendarOccurrence.Id</c>; a task id opens the task editor).</summary>
    Task OpenAsync(string occurrenceId, CancellationToken ct);

    Task OpenNewTaskAsync(string? calendarId, DateOnly? due, CancellationToken ct);

    /// <summary>The "this occurrence / this and following / all" question (I-300). Null when the user cancelled. <paramref name="forDelete"/> changes the wording and offers "Notify guests" (the returned flag).</summary>
    Task<(EditScope Scope, bool Notify)?> AskScopeAsync(string title, bool forDelete, bool hasGuests, CancellationToken ct);
}
