// Mirrors: Rust ctxmenu.rs:249-:279 (event, slot and task menu actions), calendar.rs:972 rsvp, :1002 event_ics, :830 toggle_task, :921 set_task_due; Bulwark components/calendar/event-context-menu.tsx, empty-space-context-menu.tsx, task-context-menu.tsx, calendar-sidebar-panel.tsx (calendar menu);
// B-CAL-029..039, 051, 057; I-305, I-326..I-330, I-344. The commands the calendar menus run: the argument is the menu's subject (see CalendarArg).

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>Creates the commands of the event, slot, task and calendar menus.</summary>
public static class CalendarMenuCommands
{
    private static readonly Result<Unit> Done = Result<Unit>.Ok(default);

    public static IReadOnlyList<IAppCommand> Create(CalendarCommandDeps d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return [.. EventCommands(d), .. SlotAndTaskCommands(d), .. CalendarRowCommands(d)];
    }

    private static JsonNode? EventOf(CalendarCommandDeps d, string? subject) =>
        subject is null ? null : d.Source.Events.FirstOrDefault(e => string.Equals(CalendarJson.Id(e), CalendarJson.BaseId(subject), StringComparison.Ordinal));

    private static JsonNode? CalendarOf(CalendarCommandDeps d, string? id) => d.Source.Calendars.FirstOrDefault(c => string.Equals(CalendarJson.Id(c), id, StringComparison.Ordinal));

    private static IEnumerable<IAppCommand> EventCommands(CalendarCommandDeps d)
    {
        yield return new DelegateAppCommand(CalendarCommandIds.EventRsvp, c => EventOf(d, CalendarArg.Split(c.Argument, 2)[1]) is not null, async (c, ct) =>
        {
            var parts = CalendarArg.Split(c.Argument, 2);
            if (EventOf(d, parts[1]) is not { } ev || d.Source.AccountKey is not { } account)
            {
                return Done;
            }

            var answered = await d.Invitations.AnswerAsync(account, ev, d.Source.Mine, parts[0], ct).ConfigureAwait(true);
            return answered.Map(_ => default(Unit));
        });
        yield return new DelegateAppCommand(CalendarCommandIds.EventEdit, c => c.Argument is not null, async (c, ct) =>
        {
            await d.Editors.OpenAsync(c.Argument!, ct).ConfigureAwait(true);
            return Done;
        });
        yield return new DelegateAppCommand(CalendarCommandIds.EventDuplicate, c => EventOf(d, c.Argument) is not null, (c, ct) =>
        {
            d.Editors.OpenDuplicateOf(c.Argument!);
            return Task.FromResult(Done);
        });
        yield return new DelegateAppCommand(CalendarCommandIds.EventExport, c => EventOf(d, c.Argument) is not null, async (c, ct) =>
        {
            var (name, text) = d.Rules.EventIcs(EventOf(d, c.Argument)!);
            _ = await d.Picker.SaveTextAsync(name, text, ct).ConfigureAwait(true);
            return Done;
        });
        yield return DelegateAppCommand.Sync(CalendarCommandIds.EventCopyTitle, c => d.Clipboard?.SetText(CalendarJson.Str(EventOf(d, c.Argument), "title")), c => EventOf(d, c.Argument) is not null);
        yield return DelegateAppCommand.Sync(CalendarCommandIds.EventJoin, c => d.Chrome.OpenLink(LinkOf(d, c.Argument)!), c => LinkOf(d, c.Argument) is not null);
        yield return DelegateAppCommand.Sync(CalendarCommandIds.EventCopyLink, c => d.Clipboard?.SetText(LinkOf(d, c.Argument)!), c => LinkOf(d, c.Argument) is not null);
        yield return DelegateAppCommand.Sync(CalendarCommandIds.EventPrint, c => d.Chrome.Print(c.Argument), c => c.Argument is not null);
        yield return new DelegateAppCommand(CalendarCommandIds.EventDelete, c => EventOf(d, c.Argument) is not null, async (c, ct) =>
        {
            var editor = d.Editors.EditorFor(c.Argument!);
            if (editor is not null)
            {
                _ = await editor.DeleteAsync(ct).ConfigureAwait(true);
            }

            return Done;
        });
    }

    private static string? LinkOf(CalendarCommandDeps d, string? subject) => EventOf(d, subject) is { } ev ? MeetingLinks.Find(string.Empty, ev) ?? MeetingLinks.Find(LocationName(ev), ev) : null;

    private static string LocationName(JsonNode ev) => ev["locations"] is JsonObject o ? o.Select(p => CalendarJson.Str(p.Value, "name")).FirstOrDefault(n => n.Length > 0) ?? string.Empty : string.Empty;

    private static IEnumerable<IAppCommand> SlotAndTaskCommands(CalendarCommandDeps d)
    {
        yield return new DelegateAppCommand(CalendarCommandIds.SlotNewEvent, c => c.Argument is not null, async (c, ct) =>
        {
            var (start, allDay) = Slot(c.Argument);
            await d.Editors.OpenNewEventAsync(new NewEventRequest(start, allDay ? start : start.AddHours(1), allDay), ct).ConfigureAwait(true);
            return Done;
        });
        yield return new DelegateAppCommand(CalendarCommandIds.SlotNewAllDay, c => c.Argument is not null, async (c, ct) =>
        {
            var (start, _) = Slot(c.Argument);
            await d.Editors.OpenNewEventAsync(new NewEventRequest(start.Date, start.Date, true), ct).ConfigureAwait(true);
            return Done;
        });
        yield return new DelegateAppCommand(CalendarCommandIds.SlotNewTask, c => c.Argument is not null, async (c, ct) =>
        {
            var (start, _) = Slot(c.Argument);
            await d.Editors.OpenNewTaskAsync(null, DateOnly.FromDateTime(start), ct).ConfigureAwait(true);
            return Done;
        });
        yield return new DelegateAppCommand(CalendarCommandIds.TaskToggle, c => c.Argument is not null, async (c, ct) =>
        {
            await d.Tasks.ToggleAsync(c.Argument!, ct).ConfigureAwait(true);
            return Done;
        });
        yield return new DelegateAppCommand(CalendarCommandIds.TaskDue, c => CalendarArg.Split(c.Argument, 2)[1].Length > 0, async (c, ct) =>
        {
            var parts = CalendarArg.Split(c.Argument, 2);
            await d.Tasks.SetDueByNameAsync(parts[1], parts[0], ct).ConfigureAwait(true);
            return Done;
        });
        yield return new DelegateAppCommand(CalendarCommandIds.TaskDelete, c => c.Argument is not null, async (c, ct) =>
        {
            await d.Tasks.DeleteAsync(c.Argument!, ct).ConfigureAwait(true);
            return Done;
        });
    }

    /// <summary>A slot subject: "yyyy-MM-ddTHH:mm", with "|allday" for a day cell.</summary>
    public static (DateTime Start, bool AllDay) Slot(string? subject)
    {
        var parts = (subject ?? string.Empty).Split('|');
        var ok = DateTime.TryParseExact(parts[0], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t);
        return (ok ? t : DateTime.Today, parts.Length > 1 && string.Equals(parts[1], "allday", StringComparison.Ordinal));
    }

    private static IEnumerable<IAppCommand> CalendarRowCommands(CalendarCommandDeps d)
    {
        yield return new DelegateAppCommand(CalendarCommandIds.CalNewEvent, c => c.Argument is not null, async (c, ct) =>
        {
            var start = Commands.CalendarCommands.NewRequest(d).Start;
            await d.Editors.OpenNewEventAsync(new NewEventRequest(start, start.AddHours(1), false, c.Argument), ct).ConfigureAwait(true);
            return Done;
        });
        yield return DelegateAppCommand.Sync(CalendarCommandIds.CalToggle, c => d.View.ToggleCalendar(c.Argument!), c => c.Argument is not null);
        yield return new DelegateAppCommand(CalendarCommandIds.CalRename, c => c.Argument is not null && d.Dialogs is not null, async (c, ct) =>
        {
            var calendar = CalendarOf(d, c.Argument);
            var name = await d.Dialogs!.ShowAsync(new PromptDialogRequest("Rename calendar", "Name", CalendarJson.Str(calendar, "name"), "Rename"), ct).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(name) || d.Source.AccountKey is not { } account)
            {
                return Done;
            }

            return await d.Admin.UpdateCalendarAsync(account, c.Argument!, new JsonObject { ["name"] = name.Trim() }, ct).ConfigureAwait(true);
        });
        yield return new DelegateAppCommand(CalendarCommandIds.CalColor, c => CalendarArg.Split(c.Argument, 2)[1].Length > 0, (c, ct) =>
        {
            var parts = CalendarArg.Split(c.Argument, 2);
            return d.Source.AccountKey is { } account ? d.Admin.UpdateCalendarAsync(account, parts[1], new JsonObject { ["color"] = parts[0] }, ct) : Task.FromResult(Done);
        });
        yield return new DelegateAppCommand(CalendarCommandIds.CalSetDefault, c => c.Argument is not null, (c, ct) =>
            d.Source.AccountKey is { } account ? d.Admin.SetDefaultAsync(account, c.Argument!, ct) : Task.FromResult(Done));
        yield return new DelegateAppCommand(CalendarCommandIds.CalDelete, c => c.Argument is not null && !CalendarJson.Flag(CalendarOf(d, c.Argument), "isDefault"), (c, ct) =>
            d.Source.AccountKey is { } account ? d.Admin.DeleteCalendarAsync(account, c.Argument!, ct) : Task.FromResult(Done));
    }
}
