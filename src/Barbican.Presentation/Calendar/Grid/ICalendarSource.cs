// Mirrors: Rust calendar.rs:273 load, :373 occurrences_in, :439 calendars; M4 plan section 5 (cal-views codes against the store with fakes until cal-data lands). Owner cal-views.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The calendar data as the surface needs it: what the view model reads, behind one seam (the store in the app, an in-memory list in tests and in sample runs).</summary>
public interface ICalendarSource
{
    /// <summary>Raised when calendars or events changed (a load, a push delta, a local patch or its rollback); may come from any thread.</summary>
    event EventHandler<CalendarSourceChangedEventArgs>? Changed;

    /// <summary>The login to show when no account is active (a sample run has none); null for the store.</summary>
    string? DefaultAccountKey { get; }

    /// <summary>Loads the login's calendars and events (the Retry button calls it again).</summary>
    Task<Result<Unit>> LoadAsync(string accountKey, CancellationToken ct);

    /// <summary>The login's calendars in the order the sidebar lists them (<c>Visible</c> is true: the view model owns what is hidden).</summary>
    IReadOnlyList<CalendarListRow> Calendars(string accountKey);

    /// <summary>The events, tasks (due in the range) and birthdays overlapping [from, until), on the user's clock; a snapshot nobody mutates afterwards.</summary>
    IReadOnlyList<Barbican.Core.Calendar.CalendarOccurrence> OccurrencesIn(string accountKey, DateOnly from, DateOnly until);

    /// <summary>The occurrences whose title or location contains <paramref name="query"/>, nearest to <paramref name="around"/> first (the search results list).</summary>
    IReadOnlyList<Barbican.Core.Calendar.CalendarOccurrence> Search(string accountKey, string query, DateOnly around, int max);

    /// <summary>Whether a drag may move or resize the occurrence (Bulwark calendar-editability.ts: an event with guests the user does not organise, or a read-only calendar, stays).</summary>
    bool CanMove(string accountKey, string occurrenceId);

    /// <summary>The JSCalendar object of the event the occurrence comes from (guests, description), or null.</summary>
    JsonNode? EventObject(string accountKey, string eventId);
}
