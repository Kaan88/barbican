// Composition helper: one set of calendar and task adapters per host (one undo-token table, one per-task write queue), registered method by method in the app's container.
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Calendar;

/// <summary>The calendar and task adapters of one host.</summary>
public sealed class PluginCalendarMethodSet(IPluginCalendarHost host)
{
    public IReadOnlyList<IPluginApiMethod> Methods { get; } = PluginCalendarMethods.Create(host);
}
