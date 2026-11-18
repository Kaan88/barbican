// Mirrors: Rust cal_extra.rs:666 refresh_feed, :766 sub_save, :817 sub_remove, :840 sub_refresh; Bulwark lib/calendar-subscription-sync.ts, calendar-feed-diff.ts, components/calendar/ical-subscription-modal.tsx; B-CAL-041..044, 061; I-307.
// Subscriptions are kept in the calendar's own server properties when it has them, else in the profile's calendar.json. Contract from the M4 plan section 4.3; owner cal-edit.

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Feeds;

/// <summary>Webcal subscriptions: save, refresh (diff against the stored events, no churn when nothing changed), remove.</summary>
public interface ICalendarSubscriptions
{
    Task<IReadOnlyList<CalendarSubscription>> ListAsync(string accountKey, CancellationToken ct);

    /// <summary>Checks the address (<see cref="FeedRules.Check"/>), creates the calendar when <see cref="CalendarSubscription.CalendarId"/> is empty, fetches and stores the feed; fails on a blocked address and returns the warning text of an http one with the result.</summary>
    Task<Result<string?>> SaveAsync(string accountKey, CalendarSubscription subscription, string name, CancellationToken ct);

    /// <summary>Fetches the feed (size and redirect caps, per-hop address check) and applies the difference; returns how many events changed.</summary>
    Task<Result<int>> RefreshAsync(string accountKey, string calendarId, CancellationToken ct);

    /// <summary>Removes the subscription and, when <paramref name="deleteCalendar"/>, the calendar with its events.</summary>
    Task<Result<Unit>> RemoveAsync(string accountKey, string calendarId, bool deleteCalendar, CancellationToken ct);
}
