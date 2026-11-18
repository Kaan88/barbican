// Mirrors: spec/menus.md MENU-EVENT, MENU-SLOT, MENU-TASK, MENU-CAL item by item; Rust ctxmenu.rs:249-:279; Bulwark components/calendar/event-context-menu.tsx (RSVP rows only when the user is a participant and the answer is not the current one), empty-space-context-menu.tsx,
// task-context-menu.tsx, calendar-sidebar-panel.tsx. I-326..I-330, I-344, I-305 (the menu items; the click and right-click are cal-views'). The providers read the subject from MenuContext.Subject.

using System.Text.Json.Nodes;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Menus;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>The calendar menu providers.</summary>
public static class CalendarMenus
{
    /// <summary>The confirmation text of deleting an event.</summary>
    public const string DeleteEventConfirm = "Delete this event";

    public const string DeleteCalendarConfirm = "Delete this calendar and its events";

    /// <summary>How many providers <see cref="Create"/> returns.</summary>
    public const int Count = 6;

    public static IReadOnlyList<IMenuProvider> Create(ICalendarSource source) =>
    [
        new DelegateMenuProvider(MenuIds.Event, c => Event(c, source)),
        new DelegateMenuProvider(MenuIds.Slot, Slot),
        new DelegateMenuProvider(MenuIds.Task, c => Task(c, source)),
        new DelegateMenuProvider(MenuIds.TaskDue, c => TaskDue(c, source)),
        new DelegateMenuProvider(MenuIds.Calendar, c => Calendar(c, source)),
        new DelegateMenuProvider(MenuIds.CalendarColour, c => CalendarColour(c, source)),
    ];

    private static JsonNode? EventOf(ICalendarSource source, string? subject) =>
        subject is null ? null : source.Events.FirstOrDefault(e => string.Equals(CalendarJson.Id(e), CalendarJson.BaseId(subject), StringComparison.Ordinal));

    /// <summary>The user's answer to an event: the participationStatus of the participant who is the user, or null when the user is not a guest.</summary>
    public static string? MyAnswer(JsonNode ev, IReadOnlyList<string> mine)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(mine);
        if (ev["participants"] is not JsonObject participants)
        {
            return null;
        }

        foreach (var (_, p) in participants)
        {
            var email = CalendarJson.Str(p, "email").ToLowerInvariant();
            var address = CalendarJson.Str(p, "calendarAddress").Replace("mailto:", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            var owner = p?["roles"]?["owner"] is not null;
            if (!owner && (mine.Contains(email, StringComparer.Ordinal) || mine.Contains(address, StringComparer.Ordinal)))
            {
                var status = CalendarJson.Str(p, "participationStatus");
                return status.Length > 0 ? status : "needs-action";
            }
        }

        return null;
    }

    /// <summary>MENU-EVENT.</summary>
    public static IReadOnlyList<MenuItemSpec> Event(MenuContext c, ICalendarSource source)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(source);
        if (c.Subject is not { } subject || EventOf(source, subject) is not { } ev)
        {
            return [];
        }

        var items = new List<MenuItemSpec>();
        if (MyAnswer(ev, source.Mine) is { } mine)
        {
            (string Status, string Label)[] answers = [("accepted", "Accept"), ("tentative", "Maybe"), ("declined", "Decline")];
            foreach (var (status, label) in answers.Where(a => !string.Equals(a.Status, mine, StringComparison.Ordinal)))
            {
                items.Add(Item(MenuGlyphs.Read, label, CalendarCommandIds.EventRsvp, argument: CalendarArg.Join(status, subject)));
            }

            items.Add(Sep());
        }

        var editable = source.Calendars.Any(k => string.Equals(CalendarJson.Id(k), CalendarJson.FirstCalendar(ev), StringComparison.Ordinal) && CalendarJson.Writable(k, new HashSet<string>(source.Subscriptions, StringComparer.Ordinal)));
        items.Add(Item(MenuGlyphs.Rename, "Edit", CalendarCommandIds.EventEdit, hint: "E", argument: subject));
        items.Add(Item(MenuGlyphs.Copy, "Duplicate", CalendarCommandIds.EventDuplicate, argument: subject, enabled: editable));
        items.Add(Sep());
        items.Add(Item(MenuGlyphs.Download, "Export as .ics", CalendarCommandIds.EventExport, argument: subject));
        items.Add(Item(MenuGlyphs.Copy, "Copy title", CalendarCommandIds.EventCopyTitle, argument: subject));
        if (MeetingLinks.Find(LocationName(ev), ev) is not null)
        {
            items.Add(Item(MenuGlyphs.Open, "Join meeting", CalendarCommandIds.EventJoin, argument: subject));
            items.Add(Item(MenuGlyphs.Copy, "Copy meeting link", CalendarCommandIds.EventCopyLink, argument: subject));
        }

        items.Add(Item(MenuGlyphs.Print, "Print", CalendarCommandIds.EventPrint, argument: subject));
        items.Add(Sep());
        items.Add(Danger(MenuGlyphs.Delete, "Delete", CalendarCommandIds.EventDelete, hint: "Del", confirm: DeleteEventConfirm, argument: subject, enabled: editable));
        return items;
    }

    private static string LocationName(JsonNode ev) => ev["locations"] is JsonObject o ? o.Select(p => CalendarJson.Str(p.Value, "name")).FirstOrDefault(n => n.Length > 0) ?? string.Empty : string.Empty;

    /// <summary>MENU-SLOT.</summary>
    public static IReadOnlyList<MenuItemSpec> Slot(MenuContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var subject = c.Subject ?? string.Empty;
        return
        [
            Item(MenuGlyphs.Add, "New event", CalendarCommandIds.SlotNewEvent, argument: subject),
            Item(MenuGlyphs.Add, "New all-day event", CalendarCommandIds.SlotNewAllDay, argument: subject),
            Item(MenuGlyphs.Add, "New task", CalendarCommandIds.SlotNewTask, argument: subject),
            Sep(),
            Item("Clock", "Go to today", CalendarCommandIds.Today),
        ];
    }

    private static JsonNode? TaskOf(ICalendarSource source, string? id) => source.Events.FirstOrDefault(e => string.Equals(CalendarJson.Id(e), id, StringComparison.Ordinal));

    /// <summary>MENU-TASK.</summary>
    public static IReadOnlyList<MenuItemSpec> Task(MenuContext c, ICalendarSource source)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(source);
        if (c.Subject is not { } id || TaskOf(source, id) is not { } task)
        {
            return [];
        }

        var done = string.Equals(CalendarJson.Str(task, "progress"), "completed", StringComparison.Ordinal);
        return
        [
            Item(MenuGlyphs.Read, done ? "Mark incomplete" : "Mark complete", CalendarCommandIds.TaskToggle, argument: id),
            Sub("Clock", "Due date", MenuIds.TaskDue),
            Sep(),
            Danger(MenuGlyphs.Delete, "Delete", CalendarCommandIds.TaskDelete, hint: "Del", confirm: "Delete this task", argument: id),
        ];
    }

    /// <summary>The due date flyout of MENU-TASK: Today, Tomorrow, Next week, and "No due date" only when the task has one.</summary>
    public static IReadOnlyList<MenuItemSpec> TaskDue(MenuContext c, ICalendarSource source)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(source);
        if (c.Subject is not { } id || TaskOf(source, id) is not { } task)
        {
            return [];
        }

        var items = new List<MenuItemSpec>
        {
            Item("Clock", "Today", CalendarCommandIds.TaskDue, argument: CalendarArg.Join("today", id)),
            Item("Clock", "Tomorrow", CalendarCommandIds.TaskDue, argument: CalendarArg.Join("tomorrow", id)),
            Item("Clock", "Next week", CalendarCommandIds.TaskDue, argument: CalendarArg.Join("next-week", id)),
        };
        if (CalendarJson.Str(task, "due").Length > 0)
        {
            items.Add(Item(MenuGlyphs.Cancel, "No due date", CalendarCommandIds.TaskDue, argument: CalendarArg.Join("none", id)));
        }

        return items;
    }

    private static JsonNode? CalendarOf(ICalendarSource source, string? id) => source.Calendars.FirstOrDefault(k => string.Equals(CalendarJson.Id(k), id, StringComparison.Ordinal));

    /// <summary>MENU-CAL.</summary>
    public static IReadOnlyList<MenuItemSpec> Calendar(MenuContext c, ICalendarSource source)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(source);
        if (c.Subject is not { } id || CalendarOf(source, id) is not { } cal)
        {
            return [];
        }

        var isDefault = CalendarJson.Flag(cal, "isDefault");
        var visible = !(cal["isVisible"] is JsonValue v && v.TryGetValue<bool>(out var b) && !b);
        var name = CalendarJson.Str(cal, "name");
        return
        [
            Header(name.Length > 0 ? name : "Calendar"),
            Item(MenuGlyphs.Add, "New event in this calendar", CalendarCommandIds.CalNewEvent, argument: id),
            Item("Eye", visible ? "Hide calendar" : "Show calendar", CalendarCommandIds.CalToggle, argument: id),
            Item(MenuGlyphs.Rename, "Rename", CalendarCommandIds.CalRename, argument: id),
            Sub("Drop", "Colour", MenuIds.CalendarColour),
            Item(MenuGlyphs.Default, "Set as default", CalendarCommandIds.CalSetDefault, argument: id, enabled: !isDefault),
            Sep(),
            Danger(MenuGlyphs.Delete, "Delete calendar", CalendarCommandIds.CalDelete, confirm: DeleteCalendarConfirm, argument: id, enabled: !isDefault),
        ];
    }

    /// <summary>The colour flyout of MENU-CAL: the calendar colours, the current one checked.</summary>
    public static IReadOnlyList<MenuItemSpec> CalendarColour(MenuContext c, ICalendarSource source)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(source);
        var id = c.Subject ?? string.Empty;
        var current = CalendarJson.Str(CalendarOf(source, id), "color");
        return [.. CalendarJson.Colors.Select(col => Item(
            MenuGlyphs.Dot(Convert.ToUInt32(col.Hex[1..], 16)),
            col.Name,
            CalendarCommandIds.CalColor,
            argument: CalendarArg.Join(col.Hex, id),
            check: string.Equals(current, col.Hex, StringComparison.OrdinalIgnoreCase)))];
    }
}
