// Mirrors: Rust cal_editor.rs:1037 save, :1115 scope_go, :1177 delete, calendar.rs:815 create_in, :830 toggle_task, :889 set_call, :921 set_task_due, :972 rsvp; Bulwark stores/calendar-store.ts (createEvent, updateEvent, deleteEvent), lib/recurrence-scope-edit.ts;
// JMAP Calendars CalendarEvent/set (create, update with JSON-pointer patches, destroy, sendSchedulingMessages), Stalwart crates/jmap/src/calendar_event/set.rs. B-CAL-013..017, 051, 052. Contract from the M4 plan section 4.3; owner cal-edit.

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Every CalendarEvent write (events and tasks). Each applies the plugin hooks (onBeforeEvent*, onEvent*, onTask*), writes through JMAP, and leaves the <see cref="ICalendarStore"/> consistent: a failed write is rolled back, a successful one is patched in at once and confirmed by the push delta.</summary>
public interface ICalendarWriter
{
    /// <summary>Creates or updates an event by <see cref="EditScope"/>: Single and All patch the event; Occurrence writes an override (<c>EventEditor.OverridePatch</c>); Following truncates the master and creates the new series, restoring the master when the second call fails. Returns the event id.</summary>
    Task<Result<string>> SaveEventAsync(EventWrite write, CancellationToken ct);

    /// <summary>Deletes by scope: Occurrence adds an excluded override, Following truncates the series, All and Single destroy. <paramref name="notify"/> sends cancellations when the user organises and there are guests.</summary>
    Task<Result<Unit>> DeleteEventAsync(string accountKey, string eventId, EditScope scope, string? recurrenceId, bool notify, CancellationToken ct);

    /// <summary>Drag move and resize (15 minute steps are the view's): patches start and duration (the recurrence id's override for an occurrence of a series), skipped when nothing changes, sends scheduling messages when the user organises an event with guests,
    /// and rolls the store back and returns the error when the server refuses (B-CAL-052).</summary>
    Task<Result<Unit>> MoveAsync(string accountKey, string occurrenceId, DateTime newStart, TimeSpan duration, CancellationToken ct);

    /// <summary>Creates any CalendarEvent object (a task, a quick-create event, a duplicate, an import) in <paramref name="calendarId"/>; returns the new id.</summary>
    Task<Result<string>> CreateAsync(string accountKey, string calendarId, System.Text.Json.Nodes.JsonObject data, CancellationToken ct);

    /// <summary>A JSON-pointer patch of one object (a task tick, a due date, a new colour for a task list).</summary>
    Task<Result<Unit>> UpdateAsync(string accountKey, string id, System.Text.Json.Nodes.JsonObject patch, CancellationToken ct);

    Task<Result<Unit>> DestroyAsync(string accountKey, string id, CancellationToken ct);

    /// <summary>onCheckEventConflicts (additive, M4 cal-edit): the warnings plugins add while the start or end changes in the editor; none without a plugin. A default member so existing fakes keep compiling.</summary>
    Task<IReadOnlyList<string>> CheckConflictsAsync(System.Text.Json.Nodes.JsonNode ev, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
}
