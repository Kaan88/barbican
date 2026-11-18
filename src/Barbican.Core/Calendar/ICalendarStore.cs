// Mirrors: Rust calendar.rs:273 load, :1308 patch_local, :437 with_raw, :438 raw_event; PORT-PLAN section 3.4 (calendar push: Calendar, CalendarEvent, CalendarEventNotification in the changes engine); G-044 (calendars live). B-CAL-001..006.
// Contract from the M4 plan section 4.2; owner cal-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar;

/// <summary>The in-memory calendar data of every signed-in login, loaded from JMAP and kept live by push. The views read it; writes go through <c>ICalendarWriter</c> (cal-edit) and arrive back here as changes.</summary>
public interface ICalendarStore
{
    /// <summary>Raised on the thread that changed the store; views marshal to the UI thread.</summary>
    event EventHandler<CalendarChangedEventArgs>? Changed;

    /// <summary>Loads (or reloads) every calendar of the login and its shared accounts, then every event in chunks of maxObjectsInGet (not one back-referenced get); a failure in a shared account is skipped, in the primary one is the result.</summary>
    Task<Result<CalendarSnapshot>> LoadAsync(string accountKey, CancellationToken ct);

    /// <summary>What was loaded, or null before the first load (or when the login has no Calendars capability).</summary>
    CalendarSnapshot? Snapshot(string accountKey);

    /// <summary>Applies /changes for Calendar and CalendarEvent (after a push state change); falls back to a full load on cannotCalculateChanges.</summary>
    Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct);

    /// <summary>An optimistic local change (a drag, a task tick): the event JSON is patched with a JSON-pointer patch and <see cref="Changed"/> is raised. Returns the previous event for <see cref="Restore"/>.</summary>
    JsonNode? Patch(string accountKey, string eventId, JsonObject patch);

    /// <summary>Puts back what <see cref="Patch"/> returned (the rollback of a failed write).</summary>
    void Restore(string accountKey, string eventId, JsonNode? previous);
}
