// Ported from Rust src/cal_extra.rs tests :959 feed_uid_is_stable_and_fills_only_missing, :972 feed_events_drop_remote_alerts (T-044, T-045), plus the webcal address rules of GAPS P0-4 / G-006 (LAN and localhost blocked, http warned; not Rust rows). M4 stream cal-edit.

using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Feeds;

public sealed class FeedRulesTests
{
    [Fact]
    [RustTest("cal_extra::feed_uid_is_stable_and_fills_only_missing")]
    public void Feed_uid_is_stable_and_fills_only_missing()
    {
        var e = J("""{"title": "Standup", "start": "2026-11-20T09:00:00"}""");
        var a = FeedRules.StableUid("https://x/a.ics", e);
        Assert.Equal(a, FeedRules.StableUid("https://x/a.ics", e));
        Assert.False(string.Equals(a, FeedRules.StableUid("https://x/b.ics", e), StringComparison.Ordinal));
        Assert.False(string.Equals(a, FeedRules.StableUid("https://x/a.ics", J("""{"title": "Standup", "start": "2026-11-21T09:00:00"}""")), StringComparison.Ordinal));
        var feed = new[] { e.DeepClone(), J("""{"uid": "keep", "title": "K"}"""), J("""{"uid": " ", "title": "B"}""") };
        FeedRules.FillUids("https://x/a.ics", feed);
        Assert.Equal(a, (string?)feed[0]["uid"]);
        Assert.Equal("keep", (string?)feed[1]["uid"]);
        Assert.StartsWith("barbican-feed-", (string?)feed[2]["uid"], StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("cal_extra::feed_events_drop_remote_alerts")]
    public void Feed_events_drop_remote_alerts()
    {
        var src = J("""{"@type": "Event", "uid": "u", "title": "T", "start": "2026-11-20T10:00:00", "alerts": {"a": {"trigger": {"offset": "-PT5M"}}}}""");
        var f = FeedRules.SanitizeFeed(src, "cal");
        Assert.Null(f["alerts"]);
        Assert.False((bool?)f["useDefaultAlerts"]);
        Assert.IsType<System.Text.Json.Nodes.JsonObject>(ImportRules.Sanitize(src, "cal")["alerts"]);
        Assert.Equal("1 change could not be applied (a: x)", FeedRules.RefreshError(["a: x"]));
        Assert.StartsWith("2 changes", FeedRules.RefreshError(["a", "b"]), StringComparison.Ordinal);
    }

    [Fact]
    public void Lan_and_localhost_feeds_are_blocked()
    {
        string[] urls =
        [
            "https://localhost/a.ics",
            "https://printer/a.ics",
            "https://127.0.0.1/a.ics",
            "https://[::1]/a.ics",
            "https://10.1.2.3/a.ics",
            "https://172.20.0.1/a.ics",
            "https://192.168.1.9/a.ics",
            "https://169.254.1.1/a.ics",
            "https://[fe80::1]/a.ics",
            "https://[fd00::1]/a.ics",
            "https://[::ffff:10.0.0.1]/a.ics",
            "https://0.0.0.0/a.ics",
            "https://user:pw@example.com/a.ics",
            "webcal://localhost/a.ics",
        ];
        foreach (var url in urls)
        {
            var check = FeedRules.Check(url);
            Assert.False(check.IsAllowed, url);
            Assert.NotNull(check.Blocked);
        }
    }

    [Fact]
    public void Public_https_is_allowed_and_http_is_allowed_with_a_warning()
    {
        var https = FeedRules.Check("webcal://example.com/a.ics");
        Assert.True(https.IsAllowed);
        Assert.Equal("https://example.com/a.ics", https.Url);
        Assert.Null(https.Warning);
        var http = FeedRules.Check("http://example.com/a.ics");
        Assert.True(http.IsAllowed);
        Assert.NotNull(http.Warning);
        Assert.False(FeedRules.Check("ftp://example.com/a.ics").IsAllowed);
        Assert.True(FeedRules.Check("https://172.32.0.1/a.ics").IsAllowed);
    }
}
