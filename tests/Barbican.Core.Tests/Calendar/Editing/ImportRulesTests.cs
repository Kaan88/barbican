// Ported from Rust src/cal_extra.rs test :1058 imports_are_cleaned (T-051). M4 stream cal-edit.

using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Editing;

public sealed class ImportRulesTests
{
    [Fact]
    [RustTest("cal_extra::imports_are_cleaned")]
    public void Imports_are_cleaned()
    {
        var src = J("""
            {"@type": "Event", "id": "x", "uid": "u", "title": "T", "start": "2026-11-20T10:00:00", "timeZone": "UTC", "calendarIds": {"old": true},
             "replyTo": {"imip": "mailto:org@x.example"}, "participants": {"p": {"name": "A", "sendTo": {"imip": "mailto:a@x.example"}, "foo": 1, "roles": {"attendee": true}}}}
            """);
        var c = ImportRules.Sanitize(src, "cal");
        Assert.Null(c["id"]);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(J("""{"cal": true}"""), c["calendarIds"]));
        Assert.Equal("PT0S", (string?)c["duration"]);
        Assert.Equal("mailto:org@x.example", (string?)c["organizerCalendarAddress"]);
        Assert.Equal("mailto:a@x.example", (string?)c["participants"]!["p"]!["calendarAddress"]);
        Assert.Null(c["participants"]!["p"]!["foo"]);
        var allDay = ImportRules.Sanitize(J("""{"start": "2026-11-20T00:00:00", "showWithoutTime": true, "timeZone": "UTC", "duration": "PT0S"}"""), "c");
        Assert.Equal("P1D", (string?)allDay["duration"]);
        Assert.Null(allDay["timeZone"]);
        Assert.Equal("https://example.com/a.ics", FeedRules.NormalizeUrl("webcal://example.com/a.ics"));
        Assert.Equal("https://example.com/a.ics", FeedRules.NormalizeUrl("WEBCALS://example.com/a.ics"));
        Assert.Null(FeedRules.NormalizeUrl("ftp://x"));
    }

    [Fact]
    public void The_alerts_prompt_asks_only_when_the_file_has_alerts()
    {
        Assert.True(ImportRules.HasAlerts([J("""{"title": "T", "alerts": {"a": {"trigger": {"offset": "-PT5M"}}}}""")]));
        Assert.False(ImportRules.HasAlerts([J("""{"title": "T"}"""), J("""{"title": "U", "alerts": {}}""")]));
    }
}
