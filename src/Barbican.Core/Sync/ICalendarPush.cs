// Mirrors: PORT-PLAN section 3.4 (push types Calendar, CalendarEvent, CalendarEventNotification), M4 plan D-M4-12; Rust main.rs:486 PUSH_TYPES (calendars were never pushed there; G-044). Owner cal-data.

using Barbican.Core.Jmap;

namespace Barbican.Core.Sync;

/// <summary>Where the mail engine of an account hands a calendar state change (a small seam so Core/Sync does not depend on the calendar store, which depends on the account manager).</summary>
public interface ICalendarPush
{
    /// <summary>Raised with the client of the account whose stream pushed, and the calendar types that changed.</summary>
    event EventHandler<CalendarPushEventArgs>? Changed;

    void Raise(IJmapClient client, IReadOnlyCollection<string> types);
}
