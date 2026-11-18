// New tests for the .ics export of one event (Rust calendar.rs:981-:1065, RFC 5545 3.1 folding and 3.3.11 escaping). M4 stream cal-edit.

using System.Text;
using Barbican.Core.Calendar.Editing;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Editing;

public sealed class IcsWriterTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 18, 9, 30, 0, TimeSpan.Zero);

    private static HashSet<string> Unfolded(string text) => new(text.Replace("\r\n ", string.Empty, StringComparison.Ordinal).Split("\r\n", StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static void Has(string expected, HashSet<string> lines) => Assert.True(lines.Contains(expected), expected + " in " + string.Join(" | ", lines));

    [Fact]
    public void A_timed_event_in_a_zone_has_a_tzid_and_crlf_lines()
    {
        var ev = J("""{"uid": "u1", "title": "Review", "start": "2026-11-20T10:00:00", "timeZone": "Europe/Berlin", "duration": "PT1H30M", "status": "confirmed"}""");
        var (name, text) = IcsWriter.Event(ev, Stamp);
        Assert.Equal("Review.ics", name);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("END:VCALENDAR\r\n", text, StringComparison.Ordinal);
        var lines = Unfolded(text);
        Has("UID:u1", lines);
        Has("DTSTAMP:20261118T093000Z", lines);
        Has("DTSTART;TZID=Europe/Berlin:20261120T100000", lines);
        Has("DURATION:PT1H30M", lines);
        Has("STATUS:CONFIRMED", lines);
        Assert.DoesNotContain(text.Split("\r\n"), l => l.Contains('\n', StringComparison.Ordinal));
    }

    [Fact]
    public void Utc_and_floating_and_all_day_starts()
    {
        Has("DTSTART:20261120T100000Z", Unfolded(IcsWriter.Event(J("""{"uid": "u", "title": "T", "start": "2026-11-20T10:00:00", "timeZone": "Etc/UTC"}"""), Stamp).Text));
        Has("DTSTART:20261120T100000", Unfolded(IcsWriter.Event(J("""{"uid": "u", "title": "T", "start": "2026-11-20T10:00:00"}"""), Stamp).Text));
        var all = Unfolded(IcsWriter.Event(J("""{"uid": "u", "title": "T", "start": "2026-11-20T00:00:00", "showWithoutTime": true, "duration": "P3D"}"""), Stamp).Text);
        Has("DTSTART;VALUE=DATE:20261120", all);
        Has("DTEND;VALUE=DATE:20261123", all);
    }

    [Fact]
    public void Text_is_escaped_for_comma_semicolon_backslash_and_newline()
    {
        var ev = J("{\"uid\": \"u\", \"title\": \"a,b;c\\\\d\", \"start\": \"2026-11-20T10:00:00\", \"description\": \"line1\\nline2\"}");
        var lines = Unfolded(IcsWriter.Event(ev, Stamp).Text);
        Has("SUMMARY:a\\,b\\;c\\\\d", lines);
        Has("DESCRIPTION:line1\\nline2", lines);
    }

    [Fact]
    public void Long_lines_fold_at_75_octets_never_inside_a_character()
    {
        var title = string.Concat(Enumerable.Repeat("ä€", 60));
        var (_, text) = IcsWriter.Event(J($$"""{"uid": "u", "title": "{{title}}", "start": "2026-11-20T10:00:00"}"""), Stamp);
        foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line);
        }

        Has("SUMMARY:" + title, Unfolded(text));
    }

    [Fact]
    public void Rules_guests_and_the_place_are_written()
    {
        var ev = J("""
            {"uid": "u", "title": "T", "start": "2026-11-20T10:00:00", "timeZone": "Europe/Berlin", "locations": {"l": {"name": "Room 4"}},
             "recurrenceRules": [{"frequency": "weekly", "interval": 2, "count": 5, "byDay": [{"day": "fr"}, {"day": "mo", "nthOfPeriod": -1}]}],
             "participants": {"o": {"name": "Olga", "email": "olga@x.example", "roles": {"owner": true}}, "a": {"email": "ann@x.example", "roles": {"attendee": true}, "participationStatus": "accepted"}}}
            """);
        var lines = Unfolded(IcsWriter.Event(ev, Stamp).Text);
        Has("LOCATION:Room 4", lines);
        Has("RRULE:FREQ=WEEKLY;INTERVAL=2;COUNT=5;BYDAY=FR,-1MO", lines);
        Has("ORGANIZER;CN=\"Olga\":mailto:olga@x.example", lines);
        Has("ATTENDEE;PARTSTAT=ACCEPTED:mailto:ann@x.example", lines);
    }
}
