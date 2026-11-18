// Contract from the M4 plan section 4.2; owner cal-data.

namespace Barbican.Core.Calendar;

/// <summary>The store changed: after a load, a /changes delta from push, or a local patch (drag, task toggle).</summary>
public sealed class CalendarChangedEventArgs(string accountKey, bool calendarsChanged, IReadOnlyList<string> eventIds) : EventArgs
{
    public string AccountKey { get; } = accountKey;

    public bool CalendarsChanged { get; } = calendarsChanged;

    /// <summary>The (namespaced) ids of the events that were added, changed or removed; empty with a full load.</summary>
    public IReadOnlyList<string> EventIds { get; } = eventIds;
}
