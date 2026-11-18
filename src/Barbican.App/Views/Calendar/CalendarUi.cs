// Mirrors: M4 plan section 4.5 (cal-views calls ICalendarEditors for every click and drag-to-create; it raises the calendar menus through IContextMenuService and never builds an item) and section 6.3 step 3 (the pointer handlers and the automation commands share one entry).

using Barbican.App.Shell;
using Barbican.Core.Common;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;

namespace Barbican.App.Views.Calendar;

/// <summary>What the calendar views do with a click, a double click, a right click or a drop: one place, so the pointer handlers and the automation commands take the same road.</summary>
internal static class CalendarUi
{
    public static CalendarViewModel Model => ViewHost.Get<CalendarViewModel>();

    public static CalendarGestures Gestures => ViewHost.Get<CalendarGestures>();

    /// <summary>Runs async work from an event handler: what it throws ends in an error toast, never in the dispatcher.</summary>
    public static void Run(Func<Task> work) => _ = Guard(work);

    private static async Task Guard(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (PortPendingException)
        {
            // a seam of another stream (editors, writer) that has not landed: the view stays as it was
        }
#pragma warning disable CA1031 // an event handler must not throw: the failure becomes a toast
        catch (Exception e)
#pragma warning restore CA1031
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Calendar: " + e.Message, IsError: true));
        }
    }

    /// <summary>A click selects (selecting is not opening, B-CAL-040).</summary>
    public static void Select(string id) => Model.SelectOccurrence(id);

    /// <summary>A double click or Enter opens the event (I-317).</summary>
    public static void Open(string id) => Run(() => Model.OpenAsync(id, CancellationToken.None));

    /// <summary>The quick-create popover for a clicked slot (I-314, I-322).</summary>
    public static void QuickCreate(DateTime start, DateTime end, bool allDay) =>
        Run(() => Gestures.CreateAsync(start, allDay ? end.AddDays(-1) : end, allDay, CancellationToken.None));

    /// <summary>The full editor for a new event (double click on a slot, the New event button).</summary>
    public static void NewEvent(DateTime start, DateTime end, bool allDay) =>
        Run(() => ViewHost.Get<ICalendarEditors>().OpenNewEventAsync(new NewEventRequest(start, end, allDay), CancellationToken.None));

    /// <summary>The menu of an occurrence, a slot or a calendar at window coordinates (MENU-EVENT, MENU-SLOT, MENU-CAL).</summary>
    public static void ShowMenu(string menuId, double x, double y, string? occurrenceId = null, DateTime? slot = null, bool allDay = false, string? calendarId = null)
    {
        var menus = ViewHost.Find<IContextMenuService>();
        var key = Model.AccountKey;
        if (menus is null || key is null)
        {
            return;
        }

        var target = new CalendarMenuTarget(key, occurrenceId, slot, allDay, calendarId);
        menus.Show(new MenuContext(menuId, [], null, null, null, null, CommandSurface.ContextMenu) { Calendar = target }, new MenuAnchor(x, y, null));
    }

    /// <summary>Runs a registered command by id (Import, Subscribe, New calendar: cal-ui's); false when nobody registered it.</summary>
    public static bool RunCommand(string id)
    {
        var registry = ViewHost.Find<ICommandRegistry>();
        if (registry is null || !registry.TryGet(id, out var command))
        {
            return false;
        }

        var context = new CommandContext(CommandSurface.Toolbar, [], null, null, Model.AccountKey);
        if (!command.CanExecute(context))
        {
            return false;
        }

        (ViewHost.Find<CommandRunner>() ?? new CommandRunner(ViewHost.Find<IToastService>())).Start(command, context);
        return true;
    }

    /// <summary>Whether a command is registered and enabled (the More menu greys what is not there yet).</summary>
    public static bool CanRun(string id) =>
        ViewHost.Find<ICommandRegistry>() is { } registry && registry.TryGet(id, out var command) && command.CanExecute(new CommandContext(CommandSurface.Toolbar, [], null, null, Model.AccountKey));
}
