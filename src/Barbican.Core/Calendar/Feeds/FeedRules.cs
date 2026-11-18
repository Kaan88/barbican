// Mirrors: Rust cal_extra.rs:630 normalize_feed_url, :740 stable_uid, :748 fill_uids, :755 sanitize_feed, :761 refresh_error; Bulwark lib/calendar-subscription-sync.ts, components/calendar/ical-subscription-modal.tsx; B-CAL-041..044, 061; GAPS P0-4 (new: LAN and localhost blocked, http warned).
// Stalwart has no feed fetcher of its own; the client fetches, so the client must not become a way into the user's network (D-M4-07).

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;

namespace Barbican.Core.Calendar.Feeds;

/// <summary>The pure rules of webcal subscriptions.</summary>
public static class FeedRules
{
    /// <summary>The refresh intervals offered, in minutes (15, 30, 60, 360, 1440); the default is 60 (B-CAL-042).</summary>
    public static IReadOnlyList<int> IntervalsMinutes { get; } = [15, 30, 60, 360, 1440];

    /// <summary>At most this many bytes of feed are read (20 MB, checked while reading and on Content-Length), after at most 5 redirects (B-CAL-041).</summary>
    public const long MaxBytes = 20 * 1024 * 1024;

    public const int MaxRedirects = 5;

    private static readonly string[] LocalSuffixes = [".localhost", ".local", ".localdomain", ".internal", ".lan", ".home.arpa", ".intranet", ".corp"];

    /// <summary>"webcal://" and "webcals://" become "https://" (case-insensitive); http and https stay; anything else is null.</summary>
    public static string? NormalizeUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var u = url.Trim();
        if (u.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase))
        {
            u = "https://" + u[10..];
        }
        else if (u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            u = "https://" + u[9..];
        }

        var ok = (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            && u.Length > 10 && !u.Any(char.IsWhiteSpace);
        return ok ? u : null;
    }

    /// <summary>G-006: refuses a host that is localhost, a name without a dot, a loopback, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16, fe80::/10), unique-local (fc00::/7) or unspecified address, an IPv4-mapped IPv6 of those, and a
    /// literal with a user name or password; an http address is allowed with a warning that the feed is not encrypted. The fetcher repeats the check on every redirect target and on the addresses the name resolves to.</summary>
    public static FeedUrlCheck Check(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var normalized = NormalizeUrl(url);
        if (normalized is null || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            return new FeedUrlCheck(null, "Enter a webcal:// or https:// address.", null);
        }

        if (uri.UserInfo.Length > 0)
        {
            return new FeedUrlCheck(null, "Addresses with a user name or password are not accepted.", null);
        }

        var host = uri.Host.Trim('[', ']').TrimEnd('.');
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsBlockedAddress(ip))
            {
                return new FeedUrlCheck(null, "Addresses on your own network or this computer are not accepted.", null);
            }
        }
        else if (host.Length == 0 || !host.Contains('.', StringComparison.Ordinal) || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || LocalSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            return new FeedUrlCheck(null, "Addresses on your own network or this computer are not accepted.", null);
        }

        var warning = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ? "This address is not encrypted (http): anyone on the way can read or change the calendar." : null;
        return new FeedUrlCheck(normalized, null, warning);
    }

    /// <summary>Whether a resolved or literal address is one a public feed never lives on: loopback, unspecified, private, link-local, unique-local, shared (100.64/10), multicast and reserved, and the IPv4-mapped form of those.</summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 0 || b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] is >= 64 and <= 127) || b[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (b[0] == 0xfe && (b[1] & 0xc0) is 0x80 or 0xc0) || (b[0] & 0xfe) == 0xfc || b[0] == 0xff;
        }

        return true;
    }

    /// <summary>A UID that is the same on every refresh for the same feed url and event content (a hash of url, title and start).</summary>
    public static string StableUid(string url, JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(ev);
        ulong h = 0xcbf29ce484222325;
        foreach (var part in new[] { url, (string?)ev["start"] ?? "", (string?)ev["title"] ?? "" })
        {
            foreach (var by in Encoding.UTF8.GetBytes(part).Append((byte)0x1f))
            {
                h = unchecked((h ^ by) * 0x100000001b3);
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"barbican-feed-{h:x16}");
    }

    /// <summary>Gives every event without a UID (missing or blank) a stable one ("barbican-feed-..."); existing UIDs are kept.</summary>
    public static void FillUids(string url, IReadOnlyList<JsonNode> feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        foreach (var e in feed)
        {
            if (string.IsNullOrWhiteSpace((string?)e["uid"]))
            {
                e["uid"] = StableUid(url, e);
            }
        }
    }

    /// <summary>A feed event for storing: id and method removed, calendarIds set, remote alerts dropped and <c>useDefaultAlerts</c> false (a subscribed calendar does not ring, B-CAL-044).</summary>
    public static JsonNode SanitizeFeed(JsonNode source, string calendarId)
    {
        var v = (JsonObject)ImportRules.Sanitize(source, calendarId);
        v.Remove("alerts");
        v["useDefaultAlerts"] = false;
        return v;
    }

    /// <summary>"1 change could not be applied (a: x)"; "2 changes could not be applied (a; b)"; at most three reasons.</summary>
    public static string RefreshError(IReadOnlyList<string> failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        var n = failed.Count;
        return string.Create(CultureInfo.InvariantCulture, $"{n} change{(n == 1 ? "" : "s")} could not be applied ({string.Join("; ", failed.Take(3))})");
    }
}
