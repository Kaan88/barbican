// Ported from Rust src/calendar.rs test :709 occurrences_carry_recurrence_ids_and_skip_tasks (T-055). M4 stream cal-data.

using Barbican.Core.Calendar;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar;

public sealed class CalendarOccurrencesTests
{
    [Fact]
    [Red("cal-data")]
    [RustTest("calendar::occurrences_carry_recurrence_ids_and_skip_tasks")]
    public void Occurrences_carry_recurrence_ids_and_skip_tasks()
    {
        var evs = new[]
        {
            J("""{"id": "e1", "@type": "Event", "title": "Weekly", "start": "2026-11-03T10:00:00", "duration": "PT1H30M", "recurrenceRules": [{"frequency": "weekly", "count": 3}], "calendarIds": {"a": true}}"""),
            J("""{"id": "e2", "@type": "Event", "title": "", "start": "2026-11-05T00:00:00", "duration": "P2D", "showWithoutTime": true, "calendarIds": {"a": true}}"""),
            J("""{"id": "t1", "@type": "Task", "title": "Pay", "due": "2026-11-06T09:00:00", "calendarIds": {"a": true}}"""),
        };
        var f = D("2026-11-01");
        var o = CalendarOccurrences.In(evs, f, f.AddDays(60));
        var weekly = o.Where(x => string.Equals(x.Base, "e1", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, weekly.Count);
        Assert.Equal("e1|2026-11-17T10:00:00", weekly[2].Id);
        Assert.Equal(90, (weekly[0].End - weekly[0].Start).TotalMinutes);
        var allday = o.First(x => string.Equals(x.Base, "e2", StringComparison.Ordinal));
        Assert.Equal("(no title)", allday.Title);
        Assert.True(CalendarOccurrences.Covers(allday, D("2026-11-06")));
        Assert.False(CalendarOccurrences.Covers(allday, D("2026-11-07")));
        Assert.DoesNotContain(o, x => string.Equals(x.Base, "t1", StringComparison.Ordinal));
    }
}
