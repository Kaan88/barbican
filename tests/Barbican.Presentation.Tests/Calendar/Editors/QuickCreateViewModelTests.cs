// M4 stream cal-ui acceptance: the quick-create popover builds the event of Rust qc_save (all-day events free, timed ones busy in the local zone, an empty title kept), writes it in the chosen calendar and "More options" carries title and calendar to the full editor (I-324).

using Barbican.Presentation.Calendar.Editors;

namespace Barbican.Presentation.Tests.Calendar.Editors;

public sealed class QuickCreateViewModelTests
{
    private readonly FakeSource _source = new();
    private readonly FakeWriter _writer = new();

    private static DateTime Dt(string s) => DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_timed_slot_creates_a_busy_event_in_the_default_calendar()
    {
        var q = new QuickCreateViewModel(_source, _writer, new NewEventRequest(Dt("2026-11-20T09:00:00"), Dt("2026-11-20T10:00:00"), false));
        q.Title = "Dentist";
        Assert.Equal(("Friday, November 20", "09:00 – 10:00"), q.Label);
        Assert.True(await q.SaveAsync(CancellationToken.None));
        var (calendar, ev) = _writer.Creates[0];
        Assert.Equal("work", calendar);
        Assert.Equal("Event", (string?)ev["@type"]);
        Assert.Equal("Dentist", (string?)ev["title"]);
        Assert.Equal("2026-11-20T09:00:00", (string?)ev["start"]);
        Assert.Equal("PT1H", (string?)ev["duration"]);
        Assert.Equal("busy", (string?)ev["freeBusyStatus"]);
        Assert.True((bool?)ev["useDefaultAlerts"]);
        Assert.NotNull(ev["timeZone"]);
        Assert.True(q.Closed.IsCompleted);
    }

    [Fact]
    public async Task Unmapped_system_zone_blocks_timed_creation_until_a_zone_is_selected()
    {
        var system = TimeZoneInfo.CreateCustomTimeZone("Unmapped/Test", TimeSpan.FromHours(3), "Test", "Test");
        var selected = "auto";
        string Zone() => Barbican.Core.Settings.Pages.SettingDisplayTimeZone.EventZone(selected, system);
        var q = new QuickCreateViewModel(_source, _writer, new NewEventRequest(Dt("2026-11-20T09:00:00"), Dt("2026-11-20T10:00:00"), false), zone: Zone(), currentZone: Zone);
        q.Title = "Keep this";
        Assert.False(await q.SaveAsync(CancellationToken.None));
        Assert.Contains("Select a supported time zone", q.Error);
        Assert.Empty(_writer.Creates);
        Assert.Equal("Keep this", q.Title);
        selected = "Asia/Tokyo";
        Assert.True(await q.SaveAsync(CancellationToken.None));
        Assert.Equal("Asia/Tokyo", (string?)Assert.Single(_writer.Creates).Data["timeZone"]);
    }

    [Fact]
    public async Task Unmapped_zone_does_not_block_all_day_quick_create()
    {
        var q = new QuickCreateViewModel(_source, _writer, new NewEventRequest(Dt("2026-11-20T00:00:00"), Dt("2026-11-20T00:00:00"), true), zone: string.Empty);
        Assert.True(await q.SaveAsync(CancellationToken.None));
        Assert.False(Assert.Single(_writer.Creates).Data.ContainsKey("timeZone"));
    }

    [Fact]
    public void Timed_quick_create_uses_the_selected_zone()
    {
        var q = new QuickCreateViewModel(_source, _writer, new NewEventRequest(Dt("2026-11-20T09:00:00"), Dt("2026-11-20T10:00:00"), false), zone: "Asia/Tokyo");
        Assert.Equal("Asia/Tokyo", (string?)q.BuildEvent()["timeZone"]);
    }

    [Fact]
    public void A_dragged_range_of_days_is_an_all_day_event_of_that_many_days()
    {
        var q = new QuickCreateViewModel(_source, _writer, new NewEventRequest(Dt("2026-11-20T00:00:00"), Dt("2026-11-22T00:00:00"), true));
        var ev = q.BuildEvent();
        Assert.Equal("P3D", (string?)ev["duration"]);
        Assert.Equal("free", (string?)ev["freeBusyStatus"]);
        Assert.True((bool?)ev["showWithoutTime"]);
        Assert.False(ev.ContainsKey("timeZone"));
        Assert.Equal("All day", q.Label.Time);
    }

    [Fact]
    public void Read_only_and_feed_calendars_are_not_offered()
    {
        var q = new QuickCreateViewModel(_source, _writer, new NewEventRequest(Dt("2026-11-20T09:00:00"), Dt("2026-11-20T10:00:00"), false));
        Assert.DoesNotContain(q.Calendars, c => string.Equals(c.Id, "feed", StringComparison.Ordinal));
    }

    [Fact]
    public void More_options_carries_the_title_and_calendar()
    {
        var q = new QuickCreateViewModel(_source, _writer, new NewEventRequest(Dt("2026-11-20T09:00:00"), Dt("2026-11-20T10:00:00"), false));
        q.Title = "Plan";
        q.CalendarId = "home";
        q.More();
        Assert.Equal("Plan", q.MoreRequest!.Title);
        Assert.Equal("home", q.MoreRequest.CalendarId);
        Assert.True(q.Closed.IsCompleted);
        Assert.Empty(_writer.Creates);
    }

    [Fact]
    public async Task A_failed_write_keeps_the_popover_with_the_error()
    {
        _writer.Fail = new Barbican.Core.Jmap.Errors.ValidationError("no");
        var q = new QuickCreateViewModel(_source, _writer, new NewEventRequest(Dt("2026-11-20T09:00:00"), Dt("2026-11-20T10:00:00"), false));
        Assert.False(await q.SaveAsync(CancellationToken.None));
        Assert.Equal("no", q.Error);
        Assert.False(q.Closed.IsCompleted);
    }
}
