// New tests for webcal subscriptions (M4 stream cal-edit acceptance, B-CAL-041..044, G-006, D-M4-E02, D-M4-E03): a blocked address is never fetched, a save creates the calendar and fills it, a refresh of an unchanged feed writes nothing,
// a changed feed creates, updates and removes events, feed events carry no alerts, remove can delete the calendar, and the hook tells every change.

using System.Text;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Common;
using Barbican.Core.Net;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Tests.Calendar.Editing;
using Barbican.Core.Tests.Settings.Prefs;

namespace Barbican.Core.Tests.Calendar.Feeds;

public sealed class CalendarSubscriptionsTests : IDisposable
{
    private const string Url = "https://feeds.example.com/team.ics";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "barbican-feed-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static string Feed(params (string Uid, string Title)[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Concat(events.Select(e =>
            $"BEGIN:VEVENT\r\nUID:{e.Uid}\r\nSUMMARY:{e.Title}\r\nDTSTART:20261103T100000Z\r\nDTEND:20261103T110000Z\r\nBEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT10M\r\nEND:VALARM\r\nEND:VEVENT\r\n")) + "END:VCALENDAR\r\n";

    private async Task<(CalendarRig Rig, CalendarSubscriptions Subs, FakeFeedHttp Http)> SetUpAsync()
    {
        var rig = await CalendarRig.CreateAsync();
        var http = new FakeFeedHttp();
        return (rig, new CalendarSubscriptions(rig.Accounts, rig.Admin, rig.Store, http, rig.Hooks, new TestProfilePaths(_root)), http);
    }

    [Fact]
    public async Task A_blocked_address_is_refused_before_any_request_and_an_http_one_is_saved_with_a_warning()
    {
        var (_, subs, http) = await SetUpAsync();
        foreach (var bad in new[] { "http://localhost/x.ics", "https://192.168.1.5/x.ics", "https://intranet/x.ics", "ftp://x.example.com/a.ics" })
        {
            var r = await subs.SaveAsync(CalendarRig.Account, new CalendarSubscription("", bad, 60), "Team", Ct);
            Assert.False(r.IsOk, bad);
        }

        Assert.Empty(http.Urls);
        Assert.Empty(await subs.ListAsync(CalendarRig.Account, Ct));

        http.Answer("http://plain.example.com/a.ics", Feed(("a", "Plain")));
        var warned = await subs.SaveAsync(CalendarRig.Account, new CalendarSubscription("", "http://plain.example.com/a.ics", 60), "Plain", Ct);
        Assert.True(warned.IsOk, warned.Error?.Message);
        Assert.Contains("not encrypted", warned.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_creates_the_calendar_and_fills_it_and_a_second_refresh_changes_nothing()
    {
        var (rig, subs, http) = await SetUpAsync();
        http.Answer(Url, Feed(("a", "Stand-up"), ("b", "Retro")));
        var told = await CalendarRig.HeardAsync<SubscriptionChangePayload>(rig.Hooks, HookNames.OnICalSubscriptionChange, async () =>
        {
            var r = await subs.SaveAsync(CalendarRig.Account, new CalendarSubscription("", Url, 60), "Team", Ct);
            Assert.True(r.IsOk, r.Error?.Message);
            Assert.Null(r.Value);
        });
        Assert.Equal("added", told.Change);
        var sub = Assert.Single(await subs.ListAsync(CalendarRig.Account, Ct));
        Assert.Equal(Url, sub.Url);
        Assert.Contains(rig.Cals.Calendars(), c => string.Equals((string?)c["name"], "Team", StringComparison.Ordinal));
        Assert.Equal(2, rig.Cals.Events().Count);
        Assert.All(rig.Cals.Events(), e =>
        {
            Assert.Null(e["alerts"]);
            Assert.False((bool?)e["useDefaultAlerts"]);
        });

        var sets = rig.Sets.Count;
        var again = await subs.RefreshAsync(CalendarRig.Account, sub.CalendarId, Ct);
        Assert.Equal(0, again.Value);
        Assert.Equal(sets, rig.Sets.Count);
    }

    [Fact]
    public async Task A_changed_feed_creates_updates_and_removes_only_what_differs()
    {
        var (rig, subs, http) = await SetUpAsync();
        http.Answer(Url, Feed(("a", "Stand-up"), ("b", "Retro")));
        _ = await subs.SaveAsync(CalendarRig.Account, new CalendarSubscription("", Url, 60), "Team", Ct);
        var sub = (await subs.ListAsync(CalendarRig.Account, Ct))[0];

        http.Answer(Url, Feed(("a", "Stand-up (renamed)"), ("c", "Planning")));
        var r = await subs.RefreshAsync(CalendarRig.Account, sub.CalendarId, Ct);
        Assert.Equal(3, r.Value);
        var titles = rig.Cals.Events().Select(e => (string?)e["title"]).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Planning", "Stand-up (renamed)"], titles);
    }

    [Fact]
    public async Task A_failing_feed_is_an_error_and_remove_can_delete_the_calendar()
    {
        var (rig, subs, http) = await SetUpAsync();
        http.Answer(Url, Feed(("a", "Stand-up")));
        _ = await subs.SaveAsync(CalendarRig.Account, new CalendarSubscription("", Url, 60), "Team", Ct);
        var sub = (await subs.ListAsync(CalendarRig.Account, Ct))[0];

        http.Status = 500;
        Assert.False((await subs.RefreshAsync(CalendarRig.Account, sub.CalendarId, Ct)).IsOk);
        Assert.Single(rig.Cals.Events());

        var removed = await subs.RemoveAsync(CalendarRig.Account, sub.CalendarId, true, Ct);
        Assert.True(removed.IsOk, removed.Error?.Message);
        Assert.Empty(await subs.ListAsync(CalendarRig.Account, Ct));
        Assert.DoesNotContain(rig.Cals.Calendars(), c => string.Equals((string?)c["id"], sub.CalendarId, StringComparison.Ordinal));
    }

    private sealed class FakeFeedHttp : IGuardedHttp
    {
        private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);

        public List<string> Urls { get; } = [];

        public int Status { get; set; } = 200;

        public void Answer(string url, string body) => _answers[url] = body;

        public Task<Result<GuardedResponse>> SendAsync(GuardedRequest request, CancellationToken ct)
        {
            Urls.Add(request.Uri.ToString());
            var body = _answers.GetValueOrDefault(request.Uri.ToString(), "");
            return Task.FromResult(Result<GuardedResponse>.Ok(new GuardedResponse(Status, Encoding.UTF8.GetBytes(body), "text/calendar")));
        }
    }
}
