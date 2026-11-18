// The default ICalendarPush. Owner cal-data.

using Barbican.Core.Jmap;

namespace Barbican.Core.Sync;

/// <summary>Fans a calendar push out to the store and the reminder scheduler.</summary>
public sealed class CalendarPushRelay : ICalendarPush
{
    /// <summary>The data types of the calendar the event stream is asked for besides the mail ones.</summary>
    public static readonly IReadOnlyList<string> Types = ["Calendar", "CalendarEvent", "CalendarEventNotification"];

    public event EventHandler<CalendarPushEventArgs>? Changed;

    public void Raise(IJmapClient client, IReadOnlyCollection<string> types)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(types);
        Changed?.Invoke(this, new CalendarPushEventArgs(client, types));
    }
}
