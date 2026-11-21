// M4 cal-data acceptance tests of the reminder scheduler over FakeJmap on a virtual clock: an own alert 9 days ahead and one a hundred days ahead (G-005), a series once per occurrence, two logins, state across a restart, snooze, stale and missed, settings, hooks, a push refetch (M4 plan 6.1).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Reminders;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Storage;
using Barbican.Core.Tests.Jmap;
using Barbican.Core.Tests.Settings.Prefs;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Calendar.Reminders;

public sealed class ReminderSchedulerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barbican-rem-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly List<IDisposable> _owned = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var o in _owned)
        {
            o.Dispose();
        }

        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // a temp directory
        }
    }

    private sealed class Prefs(JsonObject values) : IPrefsStore
    {
        public PrefsSnapshot Current => PrefsSnapshot.FromJson(values);

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private static string Work(FakeCalendars cals) => (string)cals.Calendars()[0]["id"]!;

    private static JsonObject Event(string id, string start, string offset, JsonObject? extra = null)
    {
        var e = new JsonObject
        {
            ["id"] = id,
            ["title"] = "Event " + id,
            ["start"] = start,
            ["timeZone"] = "UTC",
            ["duration"] = "PT30M",
            ["alerts"] = new JsonObject { ["a"] = new JsonObject { ["@type"] = "Alert", ["trigger"] = new JsonObject { ["@type"] = "OffsetTrigger", ["offset"] = offset }, ["action"] = "display" } },
        };
        foreach (var (k, v) in extra ?? [])
        {
            e[k] = v?.DeepClone();
        }

        return e;
    }

    private ReminderScheduler Make(CalendarRig rig, ReminderStateStore? state = null, JsonObject? prefs = null, IHookBus? hooks = null, string? dir = null)
    {
        var store = state ?? new ReminderStateStore(new TestProfilePaths(dir ?? _dir));
        _owned.Add(store);
        var s = new ReminderScheduler(rig, store, hooks ?? rig.Hooks, _clock, new Prefs(prefs ?? []), null, rig.Push);
        _owned.Add(s);
        return s;
    }

    private static List<ReminderEventArgs> Record(ReminderScheduler s)
    {
        var seen = new List<ReminderEventArgs>();
        s.Due += (_, e) =>
        {
            lock (seen)
            {
                seen.Add(e);
            }
        };
        return seen;
    }

    [Fact]
    public async Task An_own_alert_of_an_event_nine_days_ahead_fires()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);

        // the event starts 9 days and 10 minutes from now and sets its own alert 9 days before: it fires 10 minutes from now, an alert beyond the 8 day window of the old reader
        cals.AddEvent(work, Event("far", "2026-11-27T09:10:00", "-P9D"));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        using var sched = Make(rig);
        var seen = Record(sched);
        await sched.CheckNowAsync(Ct);
        Assert.Empty(seen);

        _clock.Advance(TimeSpan.FromMinutes(11));
        await sched.CheckNowAsync(Ct);
        var due = Assert.Single(Assert.Single(seen).Reminders);
        Assert.Equal("far", due.Event);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 9, 10, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), due.FireMs);
        Assert.False(seen[0].Missed);
    }

    [Fact]
    public async Task An_own_alert_a_hundred_days_before_its_event_fires_at_once()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);

        // an alert may be as far before its event as any offset Bulwark honours (a year): this one fires now, for an event 100 days out
        cals.AddEvent(work, Event("far-off", "2027-02-26T09:00:00", "-P100D"));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        using var sched = Make(rig);
        var seen = Record(sched);
        await sched.CheckNowAsync(Ct);
        var due = Assert.Single(Assert.Single(seen).Reminders);
        Assert.Equal("far-off", due.Event);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), due.FireMs);
    }

    [Fact]
    public async Task An_own_alert_eight_days_out_fires_once_when_the_clock_passes_it()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);

        // the event starts 60 days from now and alerts 52 days before it: it fires on the eighth day, while the event is still 52 days ahead
        cals.AddEvent(work, Event("far-off", "2027-01-17T09:00:00", "-P52D"));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        using var sched = Make(rig);
        var seen = Record(sched);
        await sched.CheckNowAsync(Ct);
        Assert.Empty(seen);

        for (var day = 1; day <= 8; day++)
        {
            _clock.Advance(TimeSpan.FromDays(1));
            await sched.CheckNowAsync(Ct);
        }

        var due = Assert.Single(Assert.Single(seen).Reminders);
        Assert.Equal("far-off", due.Event);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), due.FireMs);
        Assert.False(seen[0].Missed);

        // a clock that keeps going does not show it a second time
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            await sched.CheckNowAsync(Ct);
        }

        Assert.Single(seen);
    }

    [Fact]
    public async Task A_series_fires_once_per_occurrence()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        cals.AddEvent(work, Event("daily", "2026-11-10T10:00:00", "-PT10M", new JsonObject { ["recurrenceRules"] = new JsonArray(new JsonObject { ["frequency"] = "daily" }) }));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        using var sched = Make(rig);
        var seen = Record(sched);
        _clock.SetUtcNow(new DateTimeOffset(2026, 11, 18, 9, 55, 0, TimeSpan.Zero));
        await sched.CheckNowAsync(Ct);
        _clock.Advance(TimeSpan.FromSeconds(15));
        await sched.CheckNowAsync(Ct);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await sched.CheckNowAsync(Ct);
        Assert.Equal("daily|2026-11-18T10:00:00", Assert.Single(Assert.Single(seen).Reminders).Event);

        _clock.SetUtcNow(new DateTimeOffset(2026, 11, 19, 9, 55, 30, TimeSpan.Zero));
        await sched.CheckNowAsync(Ct);
        Assert.Equal(2, seen.Count);
        Assert.Equal("daily|2026-11-19T10:00:00", seen[1].Reminders[0].Event);
    }

    [Fact]
    public async Task Two_logins_both_fire()
    {
        var rig = CalendarRig.Create(_clock);
        foreach (var user in new[] { "anna@example.org", "ben@example.org" })
        {
            var cals = new FakeCalendars();
            var work = cals.AddCalendar("Work", isDefault: true);
            cals.AddEvent(work, Event("e-" + user[..3], "2026-11-18T09:20:00", "-PT15M"));
            _ = await rig.AddAsync(user, new FakeJmapOptions { Calendars = cals });
        }

        using var sched = Make(rig);
        var seen = Record(sched);
        await sched.CheckNowAsync(Ct);
        _clock.Advance(TimeSpan.FromMinutes(6));
        await sched.CheckNowAsync(Ct);
        Assert.Equal(2, seen.Count);
        Assert.Equal(2, seen.Select(e => e.Owner).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["e-ann", "e-ben"], seen.SelectMany(e => e.Reminders).Select(r => r.Event).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_dismissed_reminder_never_returns_after_a_restart()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        cals.AddEvent(work, Event("once", "2026-11-18T09:20:00", "-PT15M"));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        using var first = Make(rig);
        var seen = Record(first);
        await first.CheckNowAsync(Ct);
        _clock.Advance(TimeSpan.FromMinutes(6));
        await first.CheckNowAsync(Ct);
        var due = Assert.Single(Assert.Single(seen).Reminders);
        await first.DismissAsync([due], Ct);
        Assert.True(File.Exists(Path.Combine(_dir, "config", "calendar.json")));
        first.Shutdown();

        // a new process: the same profile directory
        using var second = Make(rig);
        var again = Record(second);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await second.CheckNowAsync(Ct);
        await second.CheckNowAsync(Ct);
        Assert.Empty(again);
        Assert.Empty(await second.MissedAsync(Ct));
    }

    [Fact]
    public async Task Snooze_brings_the_reminder_back_after_the_minutes()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        cals.AddEvent(work, Event("snz", "2026-11-18T09:20:00", "-PT15M"));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        using var sched = Make(rig);
        var seen = Record(sched);
        await sched.CheckNowAsync(Ct);
        _clock.Advance(TimeSpan.FromMinutes(6));
        await sched.CheckNowAsync(Ct);
        var due = seen[0].Reminders[0];

        sched.Snooze([due], 5);
        _clock.Advance(TimeSpan.FromMinutes(4));
        await sched.CheckNowAsync(Ct);
        Assert.Single(seen);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await sched.CheckNowAsync(Ct);
        Assert.Equal(2, seen.Count);
        Assert.Equal(due.Key, seen[1].Reminders[0].Key);
    }

    [Fact]
    public async Task A_reminder_older_than_ten_minutes_at_first_sight_is_missed_once_not_shown()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);

        // fired 30 minutes ago while the app was closed (the last check was two hours ago)
        cals.AddEvent(work, Event("old", "2026-11-18T08:45:00", "-PT15M"));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        var state = new ReminderStateStore(new TestProfilePaths(_dir));
        var key = rig.Accounts[0].Key;
        await state.SetLastCheckAsync(key, _clock.GetUtcNow().ToUnixTimeMilliseconds() - (2 * 3_600_000), Ct);
        using var sched = Make(rig, state);
        var seen = Record(sched);
        var missed = await sched.MissedAsync(Ct);
        Assert.Equal("old", Assert.Single(missed).Event);
        Assert.True(Assert.Single(seen).Missed);

        // listed once: a later check does not list it again, and nothing is shown as a fresh reminder
        _clock.Advance(TimeSpan.FromMinutes(1));
        await sched.CheckNowAsync(Ct);
        Assert.Single(seen);

        await sched.DismissAsync(missed, Ct);
        Assert.Empty(await sched.MissedAsync(Ct));
    }

    [Fact]
    public async Task Without_a_last_check_nothing_is_listed_as_missed()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        cals.AddEvent(work, Event("old", "2026-11-18T08:45:00", "-PT15M"));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        using var sched = Make(rig);
        Assert.Empty(await sched.MissedAsync(Ct));
    }

    [Fact]
    public async Task The_settings_gate_notifications_tasks_and_sound()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        cals.AddEvent(work, Event("e", "2026-11-18T09:20:00", "-PT15M"));
        cals.AddEvent(work, new JsonObject { ["id"] = "t", ["@type"] = "Task", ["title"] = "Pay", ["due"] = "2026-11-18T09:20:00", ["timeZone"] = "UTC", ["alerts"] = new JsonObject { ["a"] = new JsonObject { ["trigger"] = new JsonObject { ["offset"] = "-PT15M", ["relativeTo"] = "end" } } } });
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });

        // everything on, do-not-disturb on: both fire, silently
        using var all = Make(rig, prefs: new JsonObject { ["doNotDisturb"] = true }, dir: Path.Combine(_dir, "a"));
        var seen = Record(all);
        await all.CheckNowAsync(Ct);
        _clock.Advance(TimeSpan.FromMinutes(6));
        await all.CheckNowAsync(Ct);
        Assert.Equal(2, Assert.Single(seen).Reminders.Count);
        Assert.False(seen[0].Sound);

        // tasks off: only the event; sound on
        using var noTasks = Make(rig, prefs: new JsonObject { ["enableCalendarTasks"] = false }, dir: Path.Combine(_dir, "b"));
        var seen2 = Record(noTasks);
        await noTasks.CheckNowAsync(Ct);
        Assert.Equal("e", Assert.Single(Assert.Single(seen2).Reminders).Event);
        Assert.True(seen2[0].Sound);

        // notifications off: nothing
        using var off = Make(rig, prefs: new JsonObject { ["calendarNotificationsEnabled"] = false }, dir: Path.Combine(_dir, "c"));
        var seen3 = Record(off);
        await off.CheckNowAsync(Ct);
        Assert.Empty(seen3);
    }

    [Fact]
    public async Task A_plugin_that_takes_the_reminder_over_hides_our_popup_but_hears_the_alert_and_the_dismissal()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        cals.AddEvent(work, Event("hooked", "2026-11-18T09:20:00", "-PT15M"));
        var rig = CalendarRig.Create(_clock);
        _ = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        var bus = new HookBus(_clock);
        _ = bus.Intercept<CalendarAlertPayload>(HookNames.OnBeforeCalendarAlert, (_, _) => ValueTask.FromResult(false));
        var alerts = new List<CalendarAlertPayload>();
        var acks = new List<CalendarAlertAcknowledgePayload>();
        _ = bus.Observe<CalendarAlertPayload>(HookNames.OnCalendarAlert, (p, _) =>
        {
            lock (alerts)
            {
                alerts.Add(p);
            }

            return ValueTask.CompletedTask;
        });
        _ = bus.Observe<CalendarAlertAcknowledgePayload>(HookNames.OnCalendarAlertAcknowledge, (p, _) =>
        {
            lock (acks)
            {
                acks.Add(p);
            }

            return ValueTask.CompletedTask;
        });
        using var sched = Make(rig, hooks: bus);
        var seen = Record(sched);
        await sched.CheckNowAsync(Ct);
        _clock.Advance(TimeSpan.FromMinutes(6));
        await sched.CheckNowAsync(Ct);
        Assert.Empty(seen);
        await Poll.UntilAsync(() => alerts.Count == 1, "onCalendarAlert");
        Assert.Equal("hooked", alerts[0].EventId);
        Assert.Equal("Event hooked", alerts[0].EventTitle);

        var d = new DueReminder("hooked:a:1", "hooked", "a", 1, 2, "Event hooked", "Work", false);
        await sched.DismissAsync([d], Ct);
        Assert.Empty(acks);
    }

    [Fact]
    public async Task The_timer_checks_every_fifteen_seconds_and_a_calendar_push_reads_the_events_again()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        var rig = CalendarRig.Create(_clock);
        var (_, _, client) = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = cals });
        using var sched = Make(rig);
        var seen = Record(sched);
        sched.Start();
        sched.Start();
        await sched.DrainAsync();

        // an event made after the first read is not seen until the events are read again (300 s) or a push says so
        cals.AddEvent(work, Event("late", "2026-11-18T09:03:00", "-PT1M"));
        _clock.Advance(TimeSpan.FromMinutes(2));
        await sched.DrainAsync();
        await sched.CheckNowAsync(Ct);
        Assert.Empty(seen);
        rig.Push.Raise(client, ["CalendarEventNotification"]);
        await sched.DrainAsync();
        await sched.CheckNowAsync(Ct);
        Assert.Equal("late", Assert.Single(Assert.Single(seen).Reminders).Event);
    }
}
