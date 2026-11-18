// Mirrors: GAPS P0-4 / G-006; Bulwark lib/calendar-subscription-sync.ts (validation). Contract from the M4 plan section 4.3; owner cal-edit.

namespace Barbican.Core.Calendar.Feeds;

/// <summary>The verdict on a subscription address: <c>Url</c> is the https (or http) address to fetch, <c>Blocked</c> a reason when the feed must not be fetched, <c>Warning</c> text to show when it may be (plain http).</summary>
public sealed record FeedUrlCheck(string? Url, string? Blocked, string? Warning)
{
    public bool IsAllowed => Url is not null && Blocked is null;
}
