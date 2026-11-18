// New tests (M4 plan 6.3): the layout of the week grid (lanes for overlapping events, the all-day rows, the cap G-003), the month cells, the agenda rows. Owner cal-views.

#pragma warning disable MA0002, MA0006 // ids and titles are compared ordinally by nature
using Barbican.Core.Calendar;
using Barbican.Presentation.Calendar.Grid;
using static Barbican.Presentation.Tests.Calendar.Grid.CalendarHarness;

namespace Barbican.Presentation.Tests.Calendar.Grid;

public sealed class WeekLayoutTests
{
    private static CalendarOccurrence O(string id, string start, string end, bool allDay = false, string title = "t", CalendarOccurrenceKind kind = CalendarOccurrenceKind.Event) =>
        new(id, id, null, title, Dt(start), Dt(end), allDay, "work", string.Empty, kind, false);

    private static readonly IReadOnlyList<DateOnly> Week = CalendarGridRules.DayColumns(D("2026-11-16"), D("2026-11-23"));

    [Fact]
    public void A_timed_event_sits_at_its_hour_with_56_px_per_hour()
    {
        var box = WeekLayout.Timed(Week, [O("a", "2026-11-18T10:30", "2026-11-18T11:30")], 0, 24).Single();
        Assert.Equal(2, box.Column);
        Assert.Equal(10.5 * 56, box.Top);
        Assert.Equal(56, box.Height);
        Assert.Equal((0, 1), (box.Lane, box.Lanes));
        var limited = WeekLayout.Timed(Week, [O("a", "2026-11-18T10:30", "2026-11-18T11:30")], 8, 20).Single();
        Assert.Equal(2.5 * 56, limited.Top);
    }

    [Fact]
    public void Overlapping_events_share_lanes_and_a_lone_one_keeps_the_full_width()
    {
        var boxes = WeekLayout.Timed(Week, [
            O("a", "2026-11-18T10:00", "2026-11-18T12:00"),
            O("b", "2026-11-18T11:00", "2026-11-18T13:00"),
            O("c", "2026-11-18T11:30", "2026-11-18T12:00"),
            O("d", "2026-11-18T15:00", "2026-11-18T16:00")], 0, 24);
        var by = boxes.ToDictionary(b => b.Occurrence.Id);
        Assert.Equal(3, by["a"].Lanes);
        Assert.Equal([0, 1, 2], new[] { by["a"].Lane, by["b"].Lane, by["c"].Lane }.Order());
        Assert.Equal(1, by["d"].Lanes);
    }

    [Fact]
    public void A_short_event_is_drawn_at_least_twenty_minutes_tall()
    {
        var box = WeekLayout.Timed(Week, [O("a", "2026-11-18T10:00", "2026-11-18T10:05")], 0, 24).Single();
        Assert.Equal(56 * 20 / 60.0, box.Height, 6);
    }

    [Fact]
    public void An_overnight_event_continues_on_the_next_column()
    {
        var boxes = WeekLayout.Timed(Week, [O("a", "2026-11-18T22:00", "2026-11-19T07:30")], 0, 24);
        Assert.Equal([2, 3], boxes.Select(b => b.Column).Order());
        Assert.Equal(2 * 56, boxes.Single(b => b.Column == 2).Height);
        Assert.Equal(0, boxes.Single(b => b.Column == 3).Top);
    }

    [Fact]
    public void Events_outside_the_visible_hours_are_left_out()
    {
        Assert.Empty(WeekLayout.Timed(Week, [O("a", "2026-11-18T05:00", "2026-11-18T06:00"), O("b", "2026-11-18T21:00", "2026-11-18T22:00")], 8, 20));
    }

    [Fact]
    public void The_all_day_strip_stacks_spans_into_rows()
    {
        var (bars, rows) = WeekLayout.AllDay(Week, [
            O("a", "2026-11-16T00:00", "2026-11-19T00:00", allDay: true, title: "Trip"),
            O("b", "2026-11-17T00:00", "2026-11-18T00:00", allDay: true, title: "Holiday"),
            O("c", "2026-11-19T00:00", "2026-11-20T00:00", allDay: true, title: "Birthday"),
            O("d", "2026-11-18T10:00", "2026-11-18T11:00")]);
        Assert.Equal(2, rows);
        var by = bars.ToDictionary(b => b.Occurrence.Id);
        Assert.Equal((0, 3, 0), (by["a"].Column, by["a"].Span, by["a"].Row));
        Assert.Equal((1, 1, 1), (by["b"].Column, by["b"].Span, by["b"].Row));
        Assert.Equal((3, 1, 0), (by["c"].Column, by["c"].Span, by["c"].Row));
        Assert.False(by.ContainsKey("d"));
    }

    [Fact]
    public void A_day_long_timed_event_goes_to_the_strip_and_the_strip_is_capped_at_three_rows()
    {
        var many = Enumerable.Range(0, 7).Select(i => O("e" + i, "2026-11-18T00:00", "2026-11-19T00:00", allDay: true, title: "E" + i)).ToList();
        many.Add(O("long", "2026-11-18T08:00", "2026-11-19T09:00"));
        var (bars, rows) = WeekLayout.AllDay(Week, many);
        Assert.Equal(8, rows);
        Assert.Equal(8, bars.Count);
        Assert.Equal(3, CalendarGridRules.AllDayVisibleRows(rows, expanded: false));
        Assert.Equal(76, CalendarGridRules.AllDayHeight(3));
        Assert.True(CalendarGridRules.AllDayExpandable(rows));
        Assert.Equal(8, CalendarGridRules.AllDayVisibleRows(rows, expanded: true));
    }

    [Fact]
    public void The_month_grid_has_42_cells_with_four_items_then_more()
    {
        var occs = Enumerable.Range(0, 6).Select(i => O("e" + i, "2026-11-18T0" + i + ":00", "2026-11-18T0" + i + ":30")).ToList();
        occs.Add(O("span", "2026-11-17T00:00", "2026-11-20T00:00", allDay: true));
        var cells = MonthLayout.Cells(D("2026-10-26"), D("2026-11-18"), D("2026-11-18"), occs, CalendarViewSettings.Default);
        Assert.Equal(42, cells.Count);
        var c = cells.Single(x => x.Day == D("2026-11-18"));
        Assert.True(c.Today);
        Assert.True(c.InMonth);
        Assert.Equal(4, c.Items.Count);
        Assert.Equal(3, c.More);
        Assert.Equal("span", c.Items[0].Occurrence.Id);
        Assert.Single(cells.Single(x => x.Day == D("2026-11-19")).Items);
        Assert.Empty(cells.Single(x => x.Day == D("2026-11-20")).Items);
        Assert.False(cells[0].InMonth);
    }

    [Fact]
    public void Month_items_show_the_time_and_the_style_by_the_settings()
    {
        var settings = CalendarViewSettings.Read(Barbican.Core.Storage.PrefsSnapshot.FromJson(System.Text.Json.Nodes.JsonNode.Parse("""{"showTimeInMonthView": true, "calendarMonthTimedEvents": "dot", "timeFormat": "12h"}""")!.AsObject()));
        var cells = MonthLayout.Cells(D("2026-10-26"), D("2026-11-18"), D("2026-11-18"), [O("a", "2026-11-18T14:30", "2026-11-18T15:00"), O("b", "2026-11-18T00:00", "2026-11-19T00:00", allDay: true)], settings);
        var items = cells.Single(x => x.Day == D("2026-11-18")).Items;
        Assert.Equal("2:30 PM", items.Single(i => i.Occurrence.Id == "a").Time);
        Assert.Equal(MonthItemStyle.Dot, items.Single(i => i.Occurrence.Id == "a").Style);
        Assert.Equal(MonthItemStyle.Bar, items.Single(i => i.Occurrence.Id == "b").Style);
        Assert.Equal(string.Empty, items.Single(i => i.Occurrence.Id == "b").Time);
    }

    [Fact]
    public void The_agenda_lists_days_with_items_and_always_today()
    {
        var occs = new[]
        {
            O("t", "2026-11-19T00:00", "2026-11-20T00:00", allDay: true, title: "○ Pay rent", kind: CalendarOccurrenceKind.Task),
            O("b", "2026-11-19T09:00", "2026-11-19T10:00", title: "Beta"),
            O("a", "2026-11-19T09:00", "2026-11-19T10:00", title: "Alpha"),
            O("n", "2026-11-19T22:00", "2026-11-20T07:30", title: "Night"),
        };
        var rows = AgendaRules.Rows(occs, D("2026-11-18"), D("2026-12-18"), D("2026-11-18"), false);
        Assert.Equal(["Today", "Tomorrow", string.Empty, string.Empty, string.Empty, "Friday, November 20"], rows.Select(r => r.Header).ToArray());
        Assert.True(rows[0].Empty);
        Assert.Equal(["Pay rent", "Alpha", "Beta", "Night", "Night"], rows.Skip(1).Select(r => r.Title).ToArray());
        Assert.Equal("Due", rows[1].Time);
        Assert.Equal("until 07:30", rows[^1].Time);
    }

    [Fact]
    public void Overlapping_events_share_the_column_while_each_lane_stays_readable_and_cascade_when_it_would_not()
    {
        // 134 px column: two lanes share it, three would be 44 px each and cascade instead (D-M5-I05)
        Assert.Equal((0d, 134d), WeekLayout.Slot(0, 1, 134));
        Assert.Equal((67d, 67d), WeekLayout.Slot(1, 2, 134));
        var lanes = Enumerable.Range(0, 3).Select(l => WeekLayout.Slot(l, 3, 134)).ToList();
        Assert.All(lanes, s => Assert.True(s.Width >= WeekLayout.MinLaneWidth));
        Assert.Equal(0, lanes[0].Left);
        Assert.True(lanes[1].Left > lanes[0].Left && lanes[2].Left > lanes[1].Left);
        Assert.All(lanes, s => Assert.True(s.Left + s.Width <= 134.001 || s.Width == WeekLayout.MinLaneWidth));
        // a crowded column never goes below the readable width
        Assert.All(Enumerable.Range(0, 8).Select(l => WeekLayout.Slot(l, 8, 134)), s => Assert.True(s.Width >= WeekLayout.MinLaneWidth));
    }

    [Fact]
    public void Thousands_of_items_build_the_agenda_quickly()
    {
        var occs = Enumerable.Range(0, 5000).Select(i => O("e" + i, "2026-11-18T10:00", "2026-11-18T11:00", title: "Item " + i)).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var rows = AgendaRules.Rows(occs, D("2026-11-18"), D("2026-12-18"), D("2026-11-18"), false);
        watch.Stop();
        Assert.Equal(5000, rows.Count);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"the agenda took {watch.ElapsedMilliseconds} ms");
    }
}
