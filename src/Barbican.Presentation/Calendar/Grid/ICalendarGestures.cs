// Mirrors: Rust dnd.rs (events drag in 15-minute steps and resize with rollback), cal_editor.rs:1431 quick, :1455 qc_save; Bulwark components/calendar/calendar-week-view.tsx, calendar-month-view.tsx, lib/calendar/event-drag-grab.ts; B-CAL-052; I-314..I-324.
// Contract from the M4 plan section 4.5; owner cal-views implements. The writes are cal-edit's <c>ICalendarWriter.MoveAsync</c> (with the store rollback); this seam is the geometry and the decision.

using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>What a pointer drag on the grid means.</summary>
public interface ICalendarGestures
{
    /// <summary>Snaps a time to the 15-minute grid.</summary>
    DateTime Snap(DateTime time);

    /// <summary>Moves an occurrence (drag): a drop that changes nothing is skipped (returns Ok without a write), a failed write rolls the occurrence back and returns the error for a toast. Month drags move by whole days and keep the time.</summary>
    Task<Result<Unit>> MoveAsync(string occurrenceId, DateTime newStart, CancellationToken ct);

    /// <summary>Resizes the end edge in 15-minute steps (at least 15 minutes), same rules.</summary>
    Task<Result<Unit>> ResizeAsync(string occurrenceId, DateTime newEnd, CancellationToken ct);

    /// <summary>Drag-to-create (week, day and month): the range becomes the quick-create popover's start and end (<c>ICalendarEditors.OpenNewEventAsync</c>).</summary>
    Task CreateAsync(DateTime start, DateTime until, bool allDay, CancellationToken ct);
}
