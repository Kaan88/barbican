// M4 cal-data acceptance tests of the calendar store over FakeJmap: load, chunking, shared accounts, optimistic patch and restore, /changes and the full-load fallback, the hooks (M4 plan 6.1).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Service;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Calendar;

public sealed class CalendarStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeJmapOptions Options(FakeCalendars cals, IReadOnlyDictionary<string, FakeCalendars>? shared = null, int maxGet = 500) =>
        new() { Calendars = cals, SharedCalendars = shared ?? new Dictionary<string, FakeCalendars>(StringComparer.Ordinal), MaxObjectsInGet = maxGet, QueryLimitCap = maxGet };

    private static int Count(FakeJmapServer server, string method) => server.ApiCalls.Sum(c => c.Split(',').Count(m => string.Equals(m, method, StringComparison.Ordinal)));

    [Fact]
    public async Task Load_reads_calendars_and_every_event_with_the_state_strings()
    {
        var rig = CalendarRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", Options(FakeSeed.CalendarsM4()));
        var store = new CalendarStore(rig, rig.Hooks);
        Assert.Null(store.Snapshot(key));
        var r = await store.LoadAsync(key, Ct);
        Assert.True(r.IsOk);
        var s = store.Snapshot(key)!;
        Assert.Equal(["Work", "Personal", "Travel"], s.Calendars.Select(c => (string?)c["name"]).ToArray());
        Assert.Equal(5, s.Events.Count);
        Assert.StartsWith("Calendar-", s.CalendarState, StringComparison.Ordinal);
        Assert.StartsWith("CalendarEvent-", s.EventState, StringComparison.Ordinal);
        Assert.Contains(s.Events, e => string.Equals((string?)e["id"], FakeSeed.StandUpId, StringComparison.Ordinal));
        Assert.Equal(FakeSeed.StandUpId, (string?)s.Events.First(e => string.Equals((string?)e["id"], FakeSeed.StandUpId, StringComparison.Ordinal))["_rid"]);

        // the plugin hooks of a load: the tasks list is announced
        await Poll.UntilAsync(() => ((Jmap.RecordingHooks)rig.Hooks).Seen.Any(h => string.Equals(h.Hook, "onTasksLoaded", StringComparison.Ordinal)), "onTasksLoaded");
    }

    [Fact]
    public async Task Twelve_hundred_events_load_in_chunks_of_the_limit_and_paged_queries()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        FakeSeed.AddManyEvents(cals, work, 1200);
        var rig = CalendarRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", Options(cals, maxGet: 500));
        var store = new CalendarStore(rig, rig.Hooks);
        Assert.True((await store.LoadAsync(key, Ct)).IsOk);
        Assert.Equal(1200, store.Snapshot(key)!.Events.Count);

        // three chunk gets (500, 500, 200) plus the empty get that reads the state; three pages of ids; never a get over the limit (the fake answers requestTooLarge)
        Assert.Equal(4, Count(server, "CalendarEvent/get"));
        Assert.Equal(3, Count(server, "CalendarEvent/query"));
    }

    [Fact]
    public async Task A_shared_account_is_namespaced_and_one_that_fails_is_skipped()
    {
        var shared = FakeSeed.SharedCalendarM4();
        var broken = FakeSeed.SharedCalendarM4();
        broken.Fails = true;
        var rig = CalendarRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", Options(FakeSeed.CalendarsM4(), new Dictionary<string, FakeCalendars>(StringComparer.Ordinal) { ["g"] = shared, ["h"] = broken }));
        var store = new CalendarStore(rig, rig.Hooks);
        Assert.True((await store.LoadAsync(key, Ct)).IsOk);
        var s = store.Snapshot(key)!;
        var team = s.Calendars.Single(c => string.Equals((string?)c["name"], "Team", StringComparison.Ordinal));
        Assert.StartsWith("g~", (string?)team["id"], StringComparison.Ordinal);
        Assert.True((bool?)team["_shared"]);
        var offsite = s.Events.Single(e => string.Equals((string?)e["_rid"], "ev-offsite", StringComparison.Ordinal));
        Assert.Equal("g~ev-offsite", (string?)offsite["id"]);
        Assert.StartsWith("g~", ((JsonObject)offsite["calendarIds"]!).First().Key, StringComparison.Ordinal);

        // nothing of the broken account, everything of the others
        Assert.DoesNotContain(s.Calendars, c => ((string?)c["id"])!.StartsWith("h~", StringComparison.Ordinal));
        Assert.Equal(6, s.Events.Count);
    }

    [Fact]
    public async Task A_failure_in_the_primary_account_is_the_result()
    {
        var cals = FakeSeed.CalendarsM4();
        cals.Fails = true;
        var rig = CalendarRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", Options(cals));
        var store = new CalendarStore(rig, rig.Hooks);
        var r = await store.LoadAsync(key, Ct);
        Assert.False(r.IsOk);
        Assert.Null(store.Snapshot(key));
    }

    [Fact]
    public async Task Patch_then_restore_returns_the_original_and_raises_changed_twice()
    {
        var rig = CalendarRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", Options(FakeSeed.CalendarsM4()));
        var store = new CalendarStore(rig, rig.Hooks);
        _ = await store.LoadAsync(key, Ct);
        var raised = new List<CalendarChangedEventArgs>();
        store.Changed += (_, e) => raised.Add(e);
        var before = store.Snapshot(key)!;

        var previous = store.Patch(key, FakeSeed.ReviewId, new JsonObject { ["title"] = "Moved review", ["start"] = "2026-11-19T16:00:00", ["alerts/a1"] = new JsonObject { ["trigger"] = new JsonObject { ["offset"] = "-PT5M" } } });
        Assert.Equal("Design review", (string?)previous!["title"]);
        var during = store.Snapshot(key)!.Events.First(e => string.Equals((string?)e["id"], FakeSeed.ReviewId, StringComparison.Ordinal));
        Assert.Equal("Moved review", (string?)during["title"]);
        Assert.Equal("-PT5M", (string?)during["alerts"]!["a1"]!["trigger"]!["offset"]);

        // a snapshot a view already holds is not changed under it
        Assert.Equal("Design review", (string?)before.Events.First(e => string.Equals((string?)e["id"], FakeSeed.ReviewId, StringComparison.Ordinal))["title"]);

        store.Restore(key, FakeSeed.ReviewId, previous);
        var after = store.Snapshot(key)!.Events.First(e => string.Equals((string?)e["id"], FakeSeed.ReviewId, StringComparison.Ordinal));
        Assert.True(JsonNode.DeepEquals(previous, after));
        Assert.Equal(2, raised.Count);
        Assert.Equal([FakeSeed.ReviewId], raised[0].EventIds);
        Assert.False(raised[0].CalendarsChanged);

        // a patch of an occurrence id reaches the base event; an unknown id patches nothing
        Assert.NotNull(store.Patch(key, FakeSeed.StandUpId + "|2026-11-06T09:30:00", new JsonObject { ["title"] = "x" }));
        Assert.Null(store.Patch(key, "nope", new JsonObject { ["title"] = "x" }));
    }

    [Fact]
    public async Task A_push_of_CalendarEvent_makes_one_changes_call_and_the_store_shows_the_new_event()
    {
        var cals = FakeSeed.CalendarsM4();
        var rig = CalendarRig.Create();
        var (key, server, client) = await rig.AddAsync("user@example.org", Options(cals));
        var store = new CalendarStore(rig, rig.Hooks, null, rig.Push);
        _ = await store.LoadAsync(key, Ct);
        var raised = new List<CalendarChangedEventArgs>();
        store.Changed += (_, e) => raised.Add(e);

        var work = cals.Calendars().First(c => string.Equals((string?)c["name"], "Work", StringComparison.Ordinal))["id"]!.GetValue<string>();
        var id = cals.AddEvent(work, new JsonObject { ["title"] = "Pushed", ["start"] = "2026-11-25T10:00:00", ["duration"] = "PT1H" });
        var gone = FakeSeed.BirthdayId;
        Assert.True(cals.Destroy("CalendarEvent", gone));
        rig.Push.Raise(client, ["CalendarEvent"]);
        await store.DrainAsync();

        Assert.Equal(1, Count(server, "CalendarEvent/changes"));
        Assert.Equal(0, Count(server, "Calendar/changes") - 1);
        var s = store.Snapshot(key)!;
        Assert.Contains(s.Events, e => string.Equals((string?)e["id"], id, StringComparison.Ordinal));
        Assert.DoesNotContain(s.Events, e => string.Equals((string?)e["id"], gone, StringComparison.Ordinal));
        Assert.Equal(cals.State("CalendarEvent"), s.EventState);
        var change = Assert.Single(raised);
        Assert.Contains(id, change.EventIds, StringComparer.Ordinal);
        Assert.Contains(gone, change.EventIds, StringComparer.Ordinal);

        // nothing changed since: a second push asks again and finds nothing to report
        rig.Push.Raise(client, ["CalendarEvent"]);
        await store.DrainAsync();
        Assert.Single(raised);
    }

    [Fact]
    public async Task A_calendar_change_reads_the_calendars_again()
    {
        var cals = FakeSeed.CalendarsM4();
        var rig = CalendarRig.Create();
        var (key, _, client) = await rig.AddAsync("user@example.org", Options(cals));
        var store = new CalendarStore(rig, rig.Hooks, null, rig.Push);
        _ = await store.LoadAsync(key, Ct);
        _ = cals.AddCalendar("Holidays");
        rig.Push.Raise(client, ["Calendar"]);
        await store.DrainAsync();
        Assert.Equal(4, store.Snapshot(key)!.Calendars.Count);
    }

    [Fact]
    public async Task Cannot_calculate_changes_falls_back_to_a_full_load()
    {
        var cals = FakeSeed.CalendarsM4();
        var rig = CalendarRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", Options(cals));
        var store = new CalendarStore(rig, rig.Hooks);
        _ = await store.LoadAsync(key, Ct);

        // the server forgets the states it issued: /changes answers cannotCalculateChanges
        var held = store.Snapshot(key)!;
        Assert.NotNull(held.EventState);
        var work = cals.Calendars()[0]["id"]!.GetValue<string>();
        _ = cals.AddEvent(work, new JsonObject { ["id"] = "late", ["title"] = "Late", ["start"] = "2026-12-01T10:00:00" });
        cals.ExpireStates();
        var loadsBefore = Count(server, "CalendarEvent/query");
        Assert.True((await store.RefreshAsync(key, Ct)).IsOk);
        Assert.Equal(loadsBefore + 1, Count(server, "CalendarEvent/query"));
        Assert.Contains(store.Snapshot(key)!.Events, e => string.Equals((string?)e["id"], "late", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_login_without_calendars_has_no_snapshot()
    {
        var rig = CalendarRig.Create();
        var server = new FakeJmapServer(new FakeJmapOptions());
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, null, false, Ct);
        var key = Barbican.Core.Accounts.AccountKey.Of(server.Base, "user@example.org");
        var accounts = new SingleLogin(key, client);
        var store = new CalendarStore(accounts, rig.Hooks);
        var r = await store.LoadAsync(key, Ct);
        Assert.True(r.IsOk);
        Assert.Empty(r.Value!.Events);
        Assert.Null(store.Snapshot(key));
        Assert.True((await store.LoadAsync("unknown", Ct)).Error is not null);
    }

    [Fact]
    public async Task The_engine_hands_a_calendar_state_change_to_the_store_over_the_event_source()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
        var cals = FakeSeed.CalendarsM4();
        var rig = CalendarRig.Create(clock);
        var (key, server, client) = await rig.AddAsync("user@example.org", Options(cals));
        var store = new CalendarStore(rig, rig.Hooks, null, rig.Push);
        _ = await store.LoadAsync(key, Ct);
        var raised = new TaskCompletionSource<CalendarChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += (_, e) => raised.TrySetResult(e);

        using var sync = new MailSync(new MailService(client, clock), rig.Hooks, clock, new PushBackoff(_ => 0), rig.Push);
        using var stop = new CancellationTokenSource();
        var run = sync.RunAsync(stop.Token);
        await Poll.UntilAsync(() => rig.Traced("PUSH open"), "the stream to open");
        server.Push.Ping();

        // the catch-up on open already asked for the calendar types; settle it, then make a change
        clock.Advance(MailSync.Coalesce);
        await store.DrainAsync();
        var work = cals.Calendars()[0]["id"]!.GetValue<string>();
        var id = cals.AddEvent(work, new JsonObject { ["title"] = "Live", ["start"] = "2026-11-25T10:00:00" });
        await Poll.UntilAsync(() => rig.Traced("PUSH state CalendarEvent"), "the push of the new event");
        clock.Advance(MailSync.Coalesce);
        var e = await raised.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await store.DrainAsync();
        Assert.Contains(id, e.EventIds, StringComparer.Ordinal);
        Assert.Contains(store.Snapshot(key)!.Events, ev => string.Equals((string?)ev["id"], id, StringComparison.Ordinal));
        await stop.CancelAsync();
        await run;
    }

    private sealed class SingleLogin(string key, IJmapClient client) : Barbican.Core.Accounts.IAccountManager
    {
        public IReadOnlyList<Barbican.Core.Accounts.AccountInfo> Accounts => [new(key, "user@example.org", "https://x")];

        public Barbican.Core.Accounts.AccountInfo? Active => Accounts[0];

        public event EventHandler<Barbican.Core.Accounts.ActiveAccountChangedEventArgs>? ActiveChanged
        {
            add { }
            remove { }
        }

        public Task<Barbican.Core.Common.Result<Barbican.Core.Accounts.AccountInfo>> SignInWithPasswordAsync(Barbican.Core.Accounts.PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Barbican.Core.Common.Result<Barbican.Core.Accounts.AccountInfo>> SignInWithOAuthAsync(Barbican.Core.Accounts.OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        public Barbican.Core.Accounts.IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, key, StringComparison.Ordinal) ? new Session(Accounts[0], client) : null;
    }

    private sealed class Session(Barbican.Core.Accounts.AccountInfo account, IJmapClient client) : Barbican.Core.Accounts.IAccountSession
    {
        public Barbican.Core.Accounts.AccountInfo Account => account;

        public IJmapClient Client => client;

        public Barbican.Core.Mail.IMailService Mail => throw new NotSupportedException();

        public Barbican.Core.Mail.IMailSync Sync => throw new NotSupportedException();
    }
}
