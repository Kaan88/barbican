// Ported from Rust src/cal_extra.rs test :1031 birthdays (T-049). M4 stream cal-edit.

using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Editing;

public sealed class BirthdayCalendarTests
{
    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_extra::birthdays")]
    public void Birthdays()
    {
        var cards = new[]
        {
            J("""{"id": "c1", "name": {"full": "Ann Lee"}, "anniversaries": {"b": {"kind": "birth", "date": {"@type": "PartialDate", "year": 1990, "month": 11, "day": 20}}}}"""),
            J("""{"id": "c2", "name": {"full": "Leap Kid"}, "anniversaries": {"b": {"kind": "birth", "date": "2004-02-29"}, "w": {"kind": "wedding", "date": "2010-06-01"}}}"""),
            J("""{"id": "c3", "name": {"full": "No Year"}, "anniversaries": {"b": {"kind": "birth", "date": "--12-24"}}}"""),
        };
        var o = BirthdayCalendar.In(cards, D("2026-11-01"), D("2026-12-31"));
        Assert.Equal(2, o.Count);
        Assert.Equal("\U0001F382 Ann Lee (36)", o[0].Title);
        Assert.Equal("bday:c1:b:2026", o[0].Id);
        Assert.Equal(CalendarOccurrenceKind.Birthday, o[0].Kind);
        Assert.Equal("\U0001F382 No Year", o[1].Title);
        var leap = BirthdayCalendar.In(cards, D("2027-02-01"), D("2027-03-01"));
        Assert.Equal(D("2027-02-28"), DateOnly.FromDateTime(leap[0].Start));
        Assert.Equal((3, 4, (int?)1985), BirthdayCalendar.PartsOf(J("""{"@type": "Timestamp", "utc": "1985-03-04T00:00:00Z"}""")));
    }
}
