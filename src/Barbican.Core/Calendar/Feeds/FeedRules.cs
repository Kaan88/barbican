// Mirrors: Rust cal_extra.rs:630 normalize_feed_url, :740 stable_uid, :748 fill_uids, :755 sanitize_feed, :761 refresh_error; Bulwark lib/calendar-subscription-sync.ts, components/calendar/ical-subscription-modal.tsx; B-CAL-041..044, 061; GAPS P0-4 (new: LAN and localhost blocked, http warned).
// Contract from the M4 plan section 4.3; owner cal-edit replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Feeds;

/// <summary>The pure rules of webcal subscriptions.</summary>
public static class FeedRules
{
    /// <summary>The refresh intervals offered, in minutes (15, 30, 60, 360, 1440); the default is 60 (B-CAL-042).</summary>
    public static IReadOnlyList<int> IntervalsMinutes { get; } = [15, 30, 60, 360, 1440];

    /// <summary>At most this many bytes of feed are read (20 MB, checked while reading and on Content-Length), after at most 5 redirects (B-CAL-041).</summary>
    public const long MaxBytes = 20 * 1024 * 1024;

    public const int MaxRedirects = 5;

    /// <summary>"webcal://" and "webcals://" become "https://" (case-insensitive); http and https stay; anything else is null.</summary>
    public static string? NormalizeUrl(string url) => throw new PortPendingException("FeedRules.NormalizeUrl");

    /// <summary>G-006: refuses a host that is localhost, a name without a dot, a loopback, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16, fe80::/10), unique-local (fc00::/7) or unspecified address, an IPv4-mapped IPv6 of those, and a
    /// literal with a user name or password; an http address is allowed with a warning that the feed is not encrypted. The fetcher repeats the check on every redirect target and on the addresses the name resolves to.</summary>
    public static FeedUrlCheck Check(string url) => throw new PortPendingException("FeedRules.Check");

    /// <summary>A UID that is the same on every refresh for the same feed url and event content (a hash of url, title and start).</summary>
    public static string StableUid(string url, JsonNode ev) => throw new PortPendingException("FeedRules.StableUid");

    /// <summary>Gives every event without a UID (missing or blank) a stable one ("barbican-feed-..."); existing UIDs are kept.</summary>
    public static void FillUids(string url, IReadOnlyList<JsonNode> feed) => throw new PortPendingException("FeedRules.FillUids");

    /// <summary>A feed event for storing: id and method removed, calendarIds set, remote alerts dropped and <c>useDefaultAlerts</c> false (a subscribed calendar does not ring, B-CAL-044).</summary>
    public static JsonNode SanitizeFeed(JsonNode source, string calendarId) => throw new PortPendingException("FeedRules.SanitizeFeed");

    /// <summary>"1 change could not be applied (a: x)"; "2 changes could not be applied (a; b)"; at most three reasons.</summary>
    public static string RefreshError(IReadOnlyList<string> failed) => throw new PortPendingException("FeedRules.RefreshError");
}
