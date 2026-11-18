// Ported from Rust src/cal_extra.rs tests :943 agenda_titles_lose_marker_and_day_filter_toggles (T-043) and :1048 agenda_parts (T-050). M4 stream cal-views.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Presentation.Calendar.Grid;

namespace Barbican.Presentation.Tests.Calendar.Grid;

public sealed class AgendaRulesTests
{
    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime Dt(string s) => DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    [Red("cal-views")]
    [RustTest("cal_extra::agenda_titles_lose_marker_and_day_filter_toggles")]
    public void Agenda_titles_lose_marker_and_day_filter_toggles()
    {
        Assert.Equal("Pay rent", AgendaRules.TaskTitle("\u25CB Pay rent"));
        Assert.Equal("Done thing", AgendaRules.TaskTitle("\u2713 Done thing"));
        Assert.Equal("Plain", AgendaRules.TaskTitle("Plain"));
        var f = new TaskDayFilter();
        f.Toggle("2026-11-18");
        Assert.Equal(D("2026-11-18"), f.Day);
        f.Toggle("2026-11-18");
        Assert.Null(f.Day);
        f.Toggle("2026-11-19");
        f.Toggle("clear");
        Assert.Null(f.Day);
        var t = JsonNode.Parse("""{"due": "2026-11-18T09:00:00"}""")!;
        Assert.True(TaskDayFilter.IsDueOn(t, D("2026-11-18")));
        Assert.False(TaskDayFilter.IsDueOn(t, D("2026-11-19")));
    }

    [Fact]
    [Red("cal-views")]
    [RustTest("cal_extra::agenda_parts")]
    public void Agenda_parts()
    {
        var o = new CalendarOccurrence("x", "x", null, "t", Dt("2026-11-16T22:00:00"), Dt("2026-11-18T07:30:00"), false, "c", string.Empty, CalendarOccurrenceKind.Event, false);
        Assert.Equal("22:00 – 07:30", AgendaRules.TimeLabel(o, D("2026-11-16"), false));
        Assert.Equal("All day", AgendaRules.TimeLabel(o, D("2026-11-17"), false));
        Assert.Equal("until 07:30", AgendaRules.TimeLabel(o, D("2026-11-18"), false));
    }
}
