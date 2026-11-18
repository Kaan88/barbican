// Mirrors: Rust calendar.rs:872 set_color, :889 set_call; Bulwark components/calendar/calendar-sidebar-panel.tsx, create-calendar-modal.tsx; JMAP Calendars Calendar/set (create, update, destroy with onDestroyRemoveEvents), onSuccessSetIsDefault (J-072).
// I-304..I-309. Contract from the M4 plan section 4.3; owner cal-edit.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Calendar (not event) writes: create, rename, colour, visibility, default, delete.</summary>
public interface ICalendarAdmin
{
    Task<Result<string>> CreateCalendarAsync(string accountKey, string name, string color, CancellationToken ct);

    /// <summary>A pointer patch (name, color, isVisible, isSubscribed, sortOrder, defaultAlertsWithTime, defaultAlertsWithoutTime).</summary>
    Task<Result<Unit>> UpdateCalendarAsync(string accountKey, string calendarId, JsonObject patch, CancellationToken ct);

    /// <summary>Makes the calendar the default (<c>onSuccessSetIsDefault</c>).</summary>
    Task<Result<Unit>> SetDefaultAsync(string accountKey, string calendarId, CancellationToken ct);

    /// <summary>Destroys the calendar with its events (<c>onDestroyRemoveEvents</c>); the default calendar is refused.</summary>
    Task<Result<Unit>> DeleteCalendarAsync(string accountKey, string calendarId, CancellationToken ct);
}
