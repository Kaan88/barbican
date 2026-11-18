// Ported from Rust src/calendar.rs test :726 namespaced_ids (T-056). M4 stream cal-data.

using Barbican.Core.Calendar;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar;

public sealed class CalendarIdsTests
{
    [Fact]
    [RustTest("calendar::namespaced_ids")]
    public void Namespaced_ids()
    {
        Assert.Equal("a", CalendarIds.Ns("p", "p", "a"));
        Assert.Equal("x~a", CalendarIds.Ns("p", "x", "a"));
        Assert.Equal(("x", "a"), CalendarIds.SplitNs("x~a"));
        Assert.Equal((null, "a"), CalendarIds.SplitNs("a"));
        var e = J("""{"id": "e", "calendarIds": {"c": true}}""");
        CalendarIds.NamespaceEvent(e, "p", "x");
        Assert.Equal("x~e", (string?)e["id"]);
        Assert.True((bool?)e["calendarIds"]!["x~c"]);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(J("""{"c": true}"""), CalendarIds.ServerCalendarIds(e["calendarIds"]!)));
        Assert.Equal("e", CalendarIds.BaseId("e|2026-01-01T00:00:00"));
        Assert.Equal("2026-01-01T00:00:00", CalendarIds.RecurrenceIdOf("e|2026-01-01T00:00:00"));
    }
}
