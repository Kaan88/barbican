// Mirrors: Bulwark calendar-app.tsx:1954-1995 (the command list: today, previous, next, the views, new event, search, refresh, import, subscribe, print), lib/calendar-shortcuts.ts, event-modal.tsx (Mod+Enter saves, Esc closes); I-296, I-297.
// The commands the calendar keys, the palette and the toolbar run. A key whose command cannot run now is not ours (ShortcutCommands), so each CanExecute says what is possible.

using Barbican.Core.Common;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Editors.Dialogs;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>Creates the calendar commands.</summary>
public static class CalendarCommands
{
    /// <summary>All commands (navigation and editors here, the menu ones in <see cref="CalendarMenuCommands"/>).</summary>
    public static IReadOnlyList<IAppCommand> Create(CalendarCommandDeps d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return [.. Navigation(d), .. Items(d), .. CalendarMenuCommands.Create(d)];
    }

    private static IEnumerable<IAppCommand> Navigation(CalendarCommandDeps d)
    {
        yield return DelegateAppCommand.Sync(CalendarCommandIds.Today, _ => d.View.Today());
        yield return DelegateAppCommand.Sync(CalendarCommandIds.Previous, _ => d.View.GoPrevious(), _ => d.View.View != CalendarViewKind.Tasks);
        yield return DelegateAppCommand.Sync(CalendarCommandIds.Next, _ => d.View.GoNext(), _ => d.View.View != CalendarViewKind.Tasks);
        yield return View(d, CalendarCommandIds.ViewDay, CalendarViewKind.Day);
        yield return View(d, CalendarCommandIds.ViewWeek, CalendarViewKind.Week);
        yield return View(d, CalendarCommandIds.ViewMonth, CalendarViewKind.Month);
        yield return View(d, CalendarCommandIds.ViewAgenda, CalendarViewKind.Agenda);
        yield return View(d, CalendarCommandIds.ViewTasks, CalendarViewKind.Tasks);
        yield return DelegateAppCommand.Sync(CalendarCommandIds.Search, _ => d.Chrome.FocusSearch());
        yield return new DelegateAppCommand(CalendarCommandIds.Refresh, _ => true, async (_, ct) =>
        {
            await d.View.LoadAsync(ct).ConfigureAwait(true);
            return Result<Unit>.Ok(default);
        });
        yield return DelegateAppCommand.Sync(CalendarCommandIds.Print, _ => d.Chrome.Print(null));
    }

    private static void Voided(bool value) => _ = value;

    private static DelegateAppCommand View(CalendarCommandDeps d, string id, CalendarViewKind kind) => DelegateAppCommand.Sync(id, _ => d.View.SetView(kind));

    private static IEnumerable<IAppCommand> Items(CalendarCommandDeps d)
    {
        yield return new DelegateAppCommand(CalendarCommandIds.NewEvent, _ => !d.Host.EditorOpen, async (_, ct) =>
        {
            // the Tasks view's n and c make a task, every other view an event (Bulwark "new item")
            if (d.View.View == CalendarViewKind.Tasks)
            {
                await d.Editors.OpenNewTaskAsync(null, d.Tasks.Day, ct).ConfigureAwait(true);
            }
            else
            {
                await d.Editors.OpenNewEventAsync(NewRequest(d), ct).ConfigureAwait(true);
            }

            return Result<Unit>.Ok(default);
        });
        yield return new DelegateAppCommand(CalendarCommandIds.NewTask, _ => !d.Host.EditorOpen, async (_, ct) =>
        {
            await d.Editors.OpenNewTaskAsync(null, d.Tasks.Day ?? (d.View.View == CalendarViewKind.Tasks ? null : d.View.Anchor), ct).ConfigureAwait(true);
            return Result<Unit>.Ok(default);
        });
        yield return new DelegateAppCommand(CalendarCommandIds.EditEvent, c => !d.Host.EditorOpen && (c.Argument ?? d.View.SelectedId) is not null, async (c, ct) =>
        {
            if ((c.Argument ?? d.View.SelectedId) is { } id)
            {
                await d.Editors.OpenAsync(id, ct).ConfigureAwait(true);
            }

            return Result<Unit>.Ok(default);
        });
        yield return new DelegateAppCommand(CalendarCommandIds.SaveEvent, _ => d.Host.EditorOpen, async (_, ct) =>
        {
            var saved = d.Host.Card switch
            {
                EventEditorViewModel e => await e.SaveAsync(ct).ConfigureAwait(true),
                TaskEditorViewModel t => await t.SaveAsync(ct).ConfigureAwait(true),
                _ => false,
            };
            Voided(saved);
            return Result<Unit>.Ok(default);
        });
        yield return DelegateAppCommand.Sync(CalendarCommandIds.CloseEditor, _ => d.Host.Escape(), _ => d.Host.IsOpen);
        yield return DelegateAppCommand.Sync(CalendarCommandIds.Import, _ => d.Host.Show(new ImportViewModel(d.Source, d.Importer, d.Rules, d.Picker)), _ => !d.Host.IsOpen);
        yield return DelegateAppCommand.Sync(CalendarCommandIds.Subscribe, _ => d.Host.Show(new SubscriptionViewModel(d.Source, d.Subscriptions, d.Admin, d.Rules, d.Host)), _ => !d.Host.IsOpen);
        yield return DelegateAppCommand.Sync(CalendarCommandIds.AddCalendar, _ => d.Host.Show(new CreateCalendarViewModel(d.Source, d.Admin)), _ => !d.Host.IsOpen);
    }

    /// <summary>The request of "New event": the anchor day at the next hour (today) or at nine.</summary>
    public static NewEventRequest NewRequest(CalendarCommandDeps d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var now = d.View is Barbican.Presentation.Calendar.Grid.CalendarViewModel grid ? grid.Now() : d.Time.GetLocalNow().DateTime;
        var day = d.View.Anchor;
        var start = day == DateOnly.FromDateTime(now)
            ? now.Date.AddHours(Math.Min(now.Hour + 1, 22))
            : day.ToDateTime(new TimeOnly(9, 0));
        return new NewEventRequest(start, start.AddHours(1), false);
    }
}
