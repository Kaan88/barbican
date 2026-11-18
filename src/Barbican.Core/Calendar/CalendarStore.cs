// Mirrors: Rust calendar.rs:273-340. Stub from the M4 plan section 4.2; owner cal-data replaces the class (constructor dependencies: IAccountManager, IHookBus, TimeProvider; the push engine of Core/Sync raises RefreshAsync).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar;

/// <summary>The default <see cref="ICalendarStore"/>.</summary>
public sealed class CalendarStore : ICalendarStore
{
    public event EventHandler<CalendarChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<Result<CalendarSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("CalendarStore.LoadAsync");

    public CalendarSnapshot? Snapshot(string accountKey) => throw new PortPendingException("CalendarStore.Snapshot");

    public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("CalendarStore.RefreshAsync");

    public JsonNode? Patch(string accountKey, string eventId, JsonObject patch) => throw new PortPendingException("CalendarStore.Patch");

    public void Restore(string accountKey, string eventId, JsonNode? previous) => throw new PortPendingException("CalendarStore.Restore");
}
