// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:308-321 (calendar.* and tasks.* permissions: calendar:read / calendar:write, tasks:read / tasks:write), assistant/c.ts:24-26 (calendar:respond, calendar.undoDelete and
// calendar.deleteEvent as calendar:write) and host-api-calendar.ts (the calls). Typed adapters over the calendar store and the one writer the calendar UI uses; no raw JMAP is exposed.
using Barbican.Core.Plugins.Api.Tasks;
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Calendar;

/// <summary>Factory for the calendar and task adapters.</summary>
public static class PluginCalendarMethods
{
    /// <summary>The number of methods <see cref="Create"/> returns (eight calendar, seven task).</summary>
    public const int Count = 15;

    /// <summary>The adapters, sharing one host (one undo-token table) and one per-task write queue.</summary>
    public static IReadOnlyList<IPluginApiMethod> Create(IPluginCalendarHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var gate = new PluginKeyedGate();
        return
        [
            new PluginCalendarListCalendars(host), new PluginCalendarGetEvents(host), new PluginCalendarGetEvent(host), new PluginCalendarCreateEvent(host),
            new PluginCalendarUpdateEvent(host), new PluginCalendarDeleteEvent(host), new PluginCalendarUndoDelete(host), new PluginCalendarRespond(host),
            new PluginTasksList(host), new PluginTasksGet(host), new PluginTasksCreate(host), new PluginTasksUpdate(host, gate), new PluginTasksDelete(host),
            new PluginTasksSetComplete(host, gate), new PluginTasksAcknowledgeAlert(host, gate),
        ];
    }
}
