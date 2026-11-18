// Mirrors: Rust cal_extra.rs:611 subscriptions, :650 sub_begin; Bulwark components/calendar/ical-subscription-modal.tsx. Contract from the M4 plan section 4.3; owner cal-edit.

namespace Barbican.Core.Calendar.Feeds;

/// <summary>A subscribed calendar: the calendar that holds the feed's events, the feed address and the refresh interval in minutes.</summary>
public sealed record CalendarSubscription(string CalendarId, string Url, int IntervalMinutes);
