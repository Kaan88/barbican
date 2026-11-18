// Mirrors: Bulwark components/calendar/calendar-app.tsx:1954-1995 (the calendar's commands), lib/calendar-shortcuts.ts, event-context-menu.tsx, empty-space-context-menu.tsx, task-context-menu.tsx; I-296, I-297, I-326..I-330, I-344.
// The ids of the calendar commands (owner cal-ui); the keys, the palette and the menus run them through the ICommandRegistry.

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>One constant per calendar command.</summary>
public static class CalendarCommandIds
{
    public const string Today = "calendar.today";

    public const string Previous = "calendar.prev";

    public const string Next = "calendar.next";

    public const string ViewDay = "calendar.view.day";

    public const string ViewWeek = "calendar.view.week";

    public const string ViewMonth = "calendar.view.month";

    public const string ViewAgenda = "calendar.view.agenda";

    public const string ViewTasks = "calendar.view.tasks";

    public const string NewEvent = "calendar.new-event";

    public const string EditEvent = "calendar.edit-event";

    public const string NewTask = "calendar.new-task";

    public const string Search = "calendar.search";

    public const string Refresh = "calendar.refresh";

    public const string Import = "calendar.import";

    public const string Subscribe = "calendar.subscribe";

    public const string AddCalendar = "calendar.add-calendar";

    public const string Print = "calendar.print";

    public const string SaveEvent = "calendar.save-event";

    public const string CloseEditor = "calendar.close-editor";

    // ---- menu commands (the argument is the subject; see CalendarArg) ----
    public const string EventRsvp = "calendar.event.rsvp";

    public const string EventEdit = "calendar.event.edit";

    public const string EventDuplicate = "calendar.event.duplicate";

    public const string EventExport = "calendar.event.export";

    public const string EventCopyTitle = "calendar.event.copy-title";

    public const string EventJoin = "calendar.event.join";

    public const string EventCopyLink = "calendar.event.copy-link";

    public const string EventPrint = "calendar.event.print";

    public const string EventDelete = "calendar.event.delete";

    public const string SlotNewEvent = "calendar.slot.new-event";

    public const string SlotNewAllDay = "calendar.slot.new-all-day";

    public const string SlotNewTask = "calendar.slot.new-task";

    public const string TaskToggle = "calendar.task.toggle";

    public const string TaskDue = "calendar.task.due";

    public const string TaskDelete = "calendar.task.delete";

    public const string CalNewEvent = "calendar.cal.new-event";

    public const string CalToggle = "calendar.cal.toggle";

    public const string CalRename = "calendar.cal.rename";

    public const string CalColor = "calendar.cal.color";

    public const string CalSetDefault = "calendar.cal.set-default";

    public const string CalDelete = "calendar.cal.delete";
}
