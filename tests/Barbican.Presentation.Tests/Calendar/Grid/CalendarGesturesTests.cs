// New tests (M4 plan 6.3): gestures: 15-minute snap, a move to the same slot is a no-op, a failed move reports and writes nothing else, resize minimum 15 minutes, drag-to-create range ordering, an event with guests the user does not organise refuses a move;
// plus the editability rule, the settings reader, the detail model and the print model. B-CAL-052, I-318. Owner cal-views.

#pragma warning disable MA0002, MA0006 // ids and titles are compared ordinally by nature
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Calendar.Grid;
using static Barbican.Presentation.Tests.Calendar.Grid.CalendarHarness;

namespace Barbican.Presentation.Tests.Calendar.Grid;

public sealed class CalendarGesturesTests
{
    private static async Task<CalendarHarness> WithEventAsync(string start = "2026-11-18T10:00", string end = "2026-11-18T11:00", bool allDay = false)
    {
        var h = new CalendarHarness();
        h.Add("a", "Stand-up", start, end, allDay: allDay);
        await h.ReloadAsync();
        return h;
    }

    [Theory]
    [InlineData("2026-11-18T10:07", "2026-11-18T10:00")]
    [InlineData("2026-11-18T10:08", "2026-11-18T10:15")]
    [InlineData("2026-11-18T10:52", "2026-11-18T10:45")]
    [InlineData("2026-11-18T10:53", "2026-11-18T11:00")]
    public async Task Times_snap_to_fifteen_minutes(string raw, string snapped)
    {
        using var h = await WithEventAsync();
        Assert.Equal(Dt(snapped), h.Gestures.Snap(Dt(raw)));
    }

    [Fact]
    public async Task A_move_snaps_and_keeps_the_duration()
    {
        using var h = await WithEventAsync();
        var r = await h.Gestures.MoveAsync("a", Dt("2026-11-19T14:08"), CancellationToken.None);
        Assert.True(r.IsOk);
        var move = Assert.Single(h.Writer.Moves);
        Assert.Equal(("acc", "a", Dt("2026-11-19T14:15"), TimeSpan.FromHours(1)), move);
    }

    [Fact]
    public async Task A_drop_that_changes_nothing_writes_nothing()
    {
        using var h = await WithEventAsync();
        var r = await h.Gestures.MoveAsync("a", Dt("2026-11-18T10:05"), CancellationToken.None);
        Assert.True(r.IsOk);
        Assert.Empty(h.Writer.Moves);
        r = await h.Gestures.ResizeAsync("a", Dt("2026-11-18T11:04"), CancellationToken.None);
        Assert.True(r.IsOk);
        Assert.Empty(h.Writer.Moves);
    }

    [Fact]
    public async Task A_failed_move_says_so_and_leaves_the_event_where_it_was()
    {
        using var h = await WithEventAsync();
        h.Writer.MoveResult = Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Fail(new TransportError("server said no", false));
        var r = await h.Gestures.MoveAsync("a", Dt("2026-11-19T10:00"), CancellationToken.None);
        Assert.False(r.IsOk);
        Assert.Contains("server said no", Assert.Single(h.Toasts.Shown).Message, StringComparison.Ordinal);
        Assert.True(h.Toasts.Shown[0].IsError);
        Assert.Equal(Dt("2026-11-18T10:00"), h.View.Occurrences.Single().Start);
    }

    [Fact]
    public async Task Resize_keeps_the_start_and_is_at_least_fifteen_minutes()
    {
        using var h = await WithEventAsync();
        await h.Gestures.ResizeAsync("a", Dt("2026-11-18T12:30"), CancellationToken.None);
        Assert.Equal(("acc", "a", Dt("2026-11-18T10:00"), TimeSpan.FromMinutes(150)), h.Writer.Moves[0]);
        await h.Gestures.ResizeAsync("a", Dt("2026-11-18T09:00"), CancellationToken.None);
        Assert.Equal(TimeSpan.FromMinutes(15), h.Writer.Moves[1].Duration);
    }

    [Fact]
    public async Task An_all_day_event_moves_by_whole_days_and_keeps_its_length()
    {
        using var h = await WithEventAsync("2026-11-18T00:00", "2026-11-20T00:00", allDay: true);
        await h.Gestures.MoveAsync("a", Dt("2026-11-23T13:00"), CancellationToken.None);
        Assert.Equal((Dt("2026-11-23T00:00"), TimeSpan.FromDays(2)), (h.Writer.Moves[0].Start, h.Writer.Moves[0].Duration));
        await h.Gestures.ResizeAsync("a", Dt("2026-11-18T00:00"), CancellationToken.None);
        Assert.Equal(TimeSpan.FromDays(1), h.Writer.Moves[1].Duration);
    }

    [Fact]
    public async Task An_event_the_user_does_not_organise_refuses_a_move()
    {
        using var h = await WithEventAsync();
        h.Source.Immovable.Add("a");
        Assert.False(h.Gestures.CanDrag("a"));
        var r = await h.Gestures.MoveAsync("a", Dt("2026-11-19T10:00"), CancellationToken.None);
        Assert.False(r.IsOk);
        Assert.Empty(h.Writer.Moves);
    }

    [Fact]
    public async Task Tasks_and_unknown_ids_do_not_move()
    {
        using var h = new CalendarHarness();
        h.Add("task:1", "○ Pay rent", "2026-11-18T00:00", "2026-11-19T00:00", allDay: true, kind: CalendarOccurrenceKind.Task);
        await h.ReloadAsync();
        Assert.False((await h.Gestures.MoveAsync("task:1", Dt("2026-11-19T00:00"), CancellationToken.None)).IsOk);
        Assert.False((await h.Gestures.MoveAsync("nope", Dt("2026-11-19T00:00"), CancellationToken.None)).IsOk);
        Assert.Empty(h.Writer.Moves);
    }

    [Fact]
    public async Task Drag_to_create_orders_the_range_snaps_it_and_opens_the_quick_create()
    {
        using var h = new CalendarHarness();
        await h.Gestures.CreateAsync(Dt("2026-11-18T11:53"), Dt("2026-11-18T10:07"), allDay: false, CancellationToken.None);
        var r = Assert.Single(h.Editors.QuickCreates);
        Assert.Equal((Dt("2026-11-18T10:00"), Dt("2026-11-18T12:00"), false), (r.Start, r.End, r.AllDay));
    }

    [Fact]
    public async Task A_click_creates_an_hour_and_a_month_range_creates_whole_days()
    {
        using var h = new CalendarHarness();
        await h.Gestures.CreateAsync(Dt("2026-11-18T09:03"), Dt("2026-11-18T09:03"), allDay: false, CancellationToken.None);
        Assert.Equal((Dt("2026-11-18T09:00"), Dt("2026-11-18T10:00")), (h.Editors.QuickCreates[0].Start, h.Editors.QuickCreates[0].End));
        await h.Gestures.CreateAsync(Dt("2026-11-20T00:00"), Dt("2026-11-18T00:00"), allDay: true, CancellationToken.None);
        Assert.Equal((Dt("2026-11-18T00:00"), Dt("2026-11-21T00:00"), true), (h.Editors.QuickCreates[1].Start, h.Editors.QuickCreates[1].End, h.Editors.QuickCreates[1].AllDay));
    }

    [Fact]
    public void Only_the_organiser_moves_an_event_with_guests()
    {
        var mine = new[] { "me@example.org" };
        Assert.True(CalendarEditability.CanMove(JsonNode.Parse("""{"title": "solo"}""")!, mine));
        Assert.True(CalendarEditability.CanMove(JsonNode.Parse("""{"participants": {"p1": {"email": "me@example.org", "roles": {"owner": true}}}}""")!, mine));
        Assert.True(CalendarEditability.CanMove(JsonNode.Parse("""{"participants": {"p1": {"email": "ME@example.org", "roles": {"owner": true}}, "p2": {"email": "x@example.org"}}}""")!, mine));
        Assert.False(CalendarEditability.CanMove(JsonNode.Parse("""{"participants": {"p1": {"sendTo": {"imip": "mailto:boss@example.org"}, "roles": {"owner": true}}, "p2": {"email": "me@example.org"}}}""")!, mine));
    }

    [Fact]
    public void The_settings_read_their_keys_and_defaults()
    {
        var d = CalendarViewSettings.Default;
        Assert.Equal((8, 20), (d.HourFrom, d.HourTo));
        Assert.Equal(500, d.HoverDelayMs);
        var s = CalendarViewSettings.Read(Barbican.Core.Storage.PrefsSnapshot.FromJson(JsonNode.Parse("""{"calendarLimitHours": false, "calendarHoverPreview": "off", "calendarWorkingDays": [1, 2, 3]}""")!.AsObject()));
        Assert.Equal((0, 24), (s.HourFrom, s.HourTo));
        Assert.Equal(-1, s.HoverDelayMs);
        Assert.Equal([1, 2, 3], s.WorkingDays);
    }

    [Fact]
    public void The_detail_lists_guests_with_their_answers()
    {
        var ev = JsonNode.Parse("""{"description": "<p>Agenda</p>", "recurrenceRules": [{}], "participants": {"p1": {"name": "Mira Okafor", "roles": {"owner": true}}, "p2": {"email": "jonas@example.org", "participationStatus": "accepted"}, "p3": {"name": "Lena Vos", "participationStatus": "tentative"}}}""")!;
        var o = new CalendarOccurrence("a", "a", null, "Design review", Dt("2026-11-19T14:00"), Dt("2026-11-19T15:30"), false, "work", "Room 4B", CalendarOccurrenceKind.Event, false);
        var detail = EventDetails.From(o, ev, new CalendarListRow("work", "acc", "Work", "#3b82f6", true, true, false, false, true), false);
        Assert.Equal("Thu 19 Nov · 14:00 – 15:30", detail.When);
        Assert.Equal("Agenda", detail.Description);
        Assert.True(detail.Repeats);
        Assert.Equal([("Mira Okafor", "MO", "Organizer"), ("jonas@example.org", "JE", "Accepted"), ("Lena Vos", "LV", "Maybe")], detail.Guests.Select(g => (g.Name, g.Initials, g.Status)).ToArray());
    }

    [Fact]
    public void The_print_model_has_a_page_per_view()
    {
        var work = new CalendarListRow("work", "acc", "Work", "#3b82f6", true, true, false, false, true);
        var occs = new[]
        {
            new CalendarOccurrence("a", "a", null, "Stand-up", Dt("2026-11-18T10:30"), Dt("2026-11-18T11:00"), false, "work", string.Empty, CalendarOccurrenceKind.Event, false),
            new CalendarOccurrence("b", "b", null, "Trip", Dt("2026-11-18T00:00"), Dt("2026-11-20T00:00"), true, "work", string.Empty, CalendarOccurrenceKind.Event, false),
        };
        var s = CalendarViewSettings.Default;
        var today = D("2026-11-18");
        var day = CalendarPrintModel.Build(CalendarViewKind.Day, today, (today, today.AddDays(1)), occs, [work], s, today);
        Assert.Equal("Wednesday 18 November", day.Title);
        var page = Assert.Single(day.Pages);
        Assert.Equal(1, page.Columns);
        Assert.Equal([("", "Trip"), ("10:30", "Stand-up")], page.Cells[0].Lines.Select(l => (l.Time, l.Text)).ToArray());
        var week = CalendarPrintModel.Build(CalendarViewKind.Week, today, (D("2026-11-16"), D("2026-11-23")), occs, [work], s, today);
        Assert.Equal((7, 7), (week.Pages.Single().Columns, week.Pages.Single().Cells.Count));
        Assert.Equal(2, week.Pages[0].Cells[2].Lines.Count);
        Assert.Single(week.Pages[0].Cells[3].Lines);
        var month = CalendarPrintModel.Build(CalendarViewKind.Month, today, (D("2026-10-26"), D("2026-12-07")), occs, [work], s, today);
        Assert.Equal(42, month.Pages.Single().Cells.Count);
        Assert.True(month.Pages[0].Cells[0].Muted);
        var many = Enumerable.Range(0, 100).Select(i => new CalendarOccurrence("e" + i, "e" + i, null, "E" + i, Dt("2026-11-18T10:00").AddDays(i % 20), Dt("2026-11-18T11:00").AddDays(i % 20), false, "work", string.Empty, CalendarOccurrenceKind.Event, false)).ToList();
        var agenda = CalendarPrintModel.Build(CalendarViewKind.Agenda, today, (today, today.AddDays(30)), many, [work], s, today);
        Assert.True(agenda.Pages.Count >= 3);
        Assert.Equal("Today", agenda.Pages[0].Cells[0].Heading);
    }
}
