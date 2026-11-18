// Ported from Rust src/cal_editor.rs tests (:1497-:1650): new event data, DST duration, guests and rules, edits keep ids and answers, organiser detection, scope patches (T-035..T-040). M4 stream cal-edit.
// The tests need the recurrence engine and time zones of stream cal-data (sync point S1).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Calendar.Reminders;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Editing;

public sealed class EventEditorTests
{
    private static CalendarPerson P(string n, string e) => new(n, e);

    private static EventForm Form() => new()
    {
        Title = " Review ",
        Start = Dt("2026-11-20T10:00:00"),
        End = Dt("2026-11-20T11:30:00"),
        TimeZone = "Europe/Berlin",
        Calendar = "a",
        UseDefaultAlerts = true,
        Send = true,
        Organizer = true,
        Privacy = 0,
        Me = ["me@x.example"],
    };

    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_editor::new_event_data")]
    public void New_event_data()
    {
        var (d, send) = EventEditor.BuildData(Form(), null, null);
        Assert.Equal("Review", (string?)d["title"]);
        Assert.Equal("2026-11-20T10:00:00", (string?)d["start"]);
        Assert.Equal("PT1H30M", (string?)d["duration"]);
        Assert.Equal("Europe/Berlin", (string?)d["timeZone"]);
        Assert.Equal("confirmed", (string?)d["status"]);
        Assert.True((bool?)d["useDefaultAlerts"]);
        Assert.False(d.ContainsKey("alerts") || d.ContainsKey("participants") || d.ContainsKey("description"));
        Assert.True(((string?)d["uid"])!.Length >= 32);
        Assert.False(send);

        // an end before the start becomes an hour
        var f = Form() with { End = Dt("2026-11-20T09:00:00") };
        Assert.Equal("PT1H", (string?)EventEditor.BuildData(f, null, null).Data["duration"]);

        // all day over three days
        f = Form() with { AllDay = true, End = Dt("2026-11-22T00:00:00") };
        (d, _) = EventEditor.BuildData(f, null, null);
        Assert.Equal("P3D", (string?)d["duration"]);
        Assert.Equal("2026-11-20T00:00:00", (string?)d["start"]);
        Assert.Null(d["timeZone"]);
    }

    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_editor::dst_aware_duration")]
    public void Dst_aware_duration()
    {
        // Europe/Berlin leaves DST on 2026-10-25 at 03:00: 01:00 -> 04:00 wall is 4 real hours
        var f = Form() with { Start = Dt("2026-10-25T01:00:00"), End = Dt("2026-10-25T04:00:00") };
        Assert.Equal("PT4H", (string?)EventEditor.BuildData(f, null, null).Data["duration"]);
    }

    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_editor::guests_rules_alerts_location")]
    public void Guests_rules_alerts_location()
    {
        var f = Form() with
        {
            Attendees = [P("Ann", "ann@x.example"), P(string.Empty, "ANN@x.example"), P(string.Empty, "bob@y.example")],
            Repeat = 5,
            Custom = RecurrenceRule.Parse(J("""{"frequency": "weekly", "interval": 2, "byDay": [{"day": "fr"}]}""")),
            UseDefaultAlerts = false,
            Rows = [new AlertEditRow(new ReminderRow(15, 1, 0))],
            Location = "https://meet.example/abc",
            Description = "Agenda",
        };
        var (d, send) = EventEditor.BuildData(f, null, null);
        Assert.True(send);
        var ps = d["participants"]!.AsObject();
        Assert.Equal(3, ps.Count);
        var org = ps.First(p => (bool?)p.Value!["roles"]!["owner"] == true).Value!;
        Assert.Equal("me@x.example", (string?)org["email"]);
        Assert.Equal("accepted", (string?)org["participationStatus"]);
        Assert.Null(org["name"]);
        Assert.Equal("mailto:me@x.example", (string?)d["organizerCalendarAddress"]);
        Assert.Equal(2, (int?)d["recurrenceRules"]![0]!["interval"]);
        Assert.False((bool?)d["useDefaultAlerts"]);
        Assert.Equal("-PT15M", (string?)d["alerts"]!.AsObject().First().Value!["trigger"]!["offset"]);
        Assert.Equal("https://meet.example/abc", (string?)d["virtualLocations"]!["vl1"]!["uri"]);
        Assert.Equal("Agenda", (string?)d["description"]);
    }

    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_editor::edits_keep_ids_answers_and_unchanged_fields")]
    public void Edits_keep_ids_answers_and_unchanged_fields()
    {
        var ev = J("""
            {"id": "e", "title": "Review", "start": "2026-11-20T10:00:00", "duration": "PT1H30M", "timeZone": "Europe/Berlin",
             "freeBusyStatus": "busy", "privacy": "public", "organizerCalendarAddress": "mailto:me@x.example", "useDefaultAlerts": true,
             "locations": {"loc": {"@type": "Location", "name": "Room 1", "coordinates": "geo:1,2"}},
             "participants": {"o": {"email": "me@x.example", "roles": {"owner": true}, "participationStatus": "accepted"},
               "p1": {"email": "ann@x.example", "roles": {"attendee": true}, "participationStatus": "accepted"}}}
            """);
        var f = Form() with { Attendees = [P(string.Empty, "ann@x.example")], Location = "Room 2" };
        var (d, _) = EventEditor.BuildData(f, ev, null);

        // not moved: Ann keeps her id and answer, the place its id and coordinates
        Assert.Equal("accepted", (string?)d["participants"]!["p1"]!["participationStatus"]);
        Assert.NotNull(d["participants"]!["o"]);
        Assert.Equal("Room 2", (string?)d["locations"]!["loc"]!["name"]);
        Assert.Equal("geo:1,2", (string?)d["locations"]!["loc"]!["coordinates"]);
        Assert.False(d.ContainsKey("freeBusyStatus") || d.ContainsKey("status") || d.ContainsKey("uid"));
        Assert.False(d.ContainsKey("organizerCalendarAddress"));

        // moved by the organizer: asked again
        var f2 = f with { Start = Dt("2026-11-21T10:00:00"), End = Dt("2026-11-21T11:30:00") };
        (d, _) = EventEditor.BuildData(f2, ev, null);
        Assert.Equal("needs-action", (string?)d["participants"]!["p1"]!["participationStatus"]);

        // guests all removed: participants and organizer cleared
        var f3 = Form() with { Location = "Room 1" };
        (d, _) = EventEditor.BuildData(f3, ev, null);
        Assert.True(d.ContainsKey("participants") && d.ContainsKey("organizerCalendarAddress"));
        Assert.Null(d["participants"]);
        Assert.Null(d["organizerCalendarAddress"]);
        Assert.False(d.ContainsKey("locations"));

        // a repeat removed takes the overrides along
        var series = ev.DeepClone();
        series["recurrenceRules"] = J("""[{"frequency": "daily"}]""");
        series["recurrenceOverrides"] = J("""{"2026-11-21T10:00:00": {"excluded": true}}""");
        (d, _) = EventEditor.BuildData(f3, series, null);
        Assert.True(d.ContainsKey("recurrenceRules") && d.ContainsKey("recurrenceOverrides"));
        Assert.Null(d["recurrenceRules"]);
        Assert.Null(d["recurrenceOverrides"]);
    }

    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_editor::organizer_detection")]
    public void Organizer_detection()
    {
        string[] me = ["me@x.example"];
        Assert.True(EventEditor.UserOrganizes(J("{}"), me));
        Assert.True(EventEditor.UserOrganizes(J("""{"participants": {"a": {"email": "ME@x.example", "roles": {"owner": true}}}}"""), me));
        Assert.False(EventEditor.UserOrganizes(J("""{"organizerCalendarAddress": "mailto:boss@y.example"}"""), me));
        Assert.True(EventEditor.UserOrganizes(J("""{"organizerCalendarAddress": "mailto:boss@y.example", "isOrigin": true}"""), me));
        var e = J("""{"participants": {"a": {"calendarAddress": "mailto:boss@y.example", "roles": {"owner": true}, "name": "Boss"}, "b": {"email": "ann@x.example"}, "c": {"email": "ANN@x.example"}}}""");
        Assert.Equal(P("Boss", "boss@y.example"), EventEditor.EventOrganizer(e));
        Assert.Equal([P(string.Empty, "ann@x.example")], EventEditor.AttendeesOf(e));
        var again = EventEditor.AskedAgain(J("""
            {"o": {"email": "boss@y.example", "roles": {"owner": true}, "participationStatus": "accepted"},
             "m": {"email": "me@x.example", "participationStatus": "accepted"}, "a": {"email": "ann@x.example", "participationStatus": "declined"}}
            """), me);
        Assert.Equal("accepted", (string?)again["o"]!["participationStatus"]);
        Assert.Equal("accepted", (string?)again["m"]!["participationStatus"]);
        Assert.Equal("needs-action", (string?)again["a"]!["participationStatus"]);
    }

    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_editor::scope_patches")]
    public void Scope_patches()
    {
        var master = J("""
            {"id": "s", "title": "Stand-up", "start": "2026-11-02T09:00:00", "duration": "PT15M", "timeZone": "UTC",
             "recurrenceRules": [{"frequency": "daily", "count": 10}], "uid": "u1",
             "participants": {"o": {"email": "me@x.example", "roles": {"owner": true}, "participationStatus": "accepted"}, "a": {"email": "ann@x.example", "participationStatus": "accepted"}}}
            """);
        var data = new JsonObject { ["title"] = "Stand-up (moved)", ["duration"] = "PT15M" };

        // this occurrence: an override under its recurrence id, the whole map when there is none
        var ov = EventEditor.OccurrenceOverride(master, "2026-11-04T09:00:00", data, Dt("2026-11-04T10:00:00"));
        Assert.Equal("Stand-up (moved)", (string?)ov["title"]);
        Assert.Equal("2026-11-04T10:00:00", (string?)ov["start"]);
        Assert.False(ov.ContainsKey("duration"));
        var p = EventEditor.OverridePatch(master, "2026-11-04T09:00:00", ov.DeepClone());
        Assert.IsType<JsonObject>(p["recurrenceOverrides"]!["2026-11-04T09:00:00"]);
        var with = master.DeepClone();
        with["recurrenceOverrides"] = J("""{"2026-11-03T09:00:00": {"excluded": true}}""");
        Assert.True(EventEditor.OverridePatch(with, "2026-11-04T09:00:00", ov).ContainsKey("recurrenceOverrides/2026-11-04T09:00:00"));

        // following: the series ends a second before, overrides from there on go
        var with2 = master.DeepClone();
        with2["recurrenceOverrides"] = J("""{"2026-11-03T09:00:00": {"title": "x"}, "2026-11-06T09:00:00": {"excluded": true}}""");
        var (t, restore) = EventEditor.TruncatePatch(with2, "2026-11-05T09:00:00");
        Assert.Equal("2026-11-05T08:59:59", (string?)t["recurrenceRules"]![0]!["until"]);
        Assert.Null(t["recurrenceRules"]![0]!["count"]);
        Assert.True(t.ContainsKey("recurrenceOverrides/2026-11-06T09:00:00") && !t.ContainsKey("recurrenceOverrides/2026-11-03T09:00:00"));
        Assert.Equal(10, (int?)restore["recurrenceRules"]![0]!["count"]);
        var n = EventEditor.FollowingSeries(master, "2026-11-05T09:00:00", data, ["me@x.example"]);
        Assert.Equal(7, (int?)n["recurrenceRules"]![0]!["count"]);
        Assert.Equal("Stand-up (moved)", (string?)n["title"]);
        Assert.False(string.Equals("u1", (string?)n["uid"], StringComparison.Ordinal));
        Assert.Equal("needs-action", (string?)n["participants"]!["a"]!["participationStatus"]);
        Assert.Equal("2026-11-05T09:00:00", (string?)n["start"]);

        // all: the series moves as far as the occurrence did; an untouched repeat stays out
        var d2 = (JsonObject)data.DeepClone();
        d2["start"] = "2026-11-04T09:30:00";
        d2["recurrenceRules"] = J("""[{"frequency": "daily", "count": 10, "interval": 1}]""");
        var s = EventEditor.SeriesUpdate(master, Dt("2026-11-04T09:00:00"), d2);
        Assert.Equal("2026-11-02T09:30:00", (string?)s["start"]);
        Assert.False(s.ContainsKey("recurrenceRules"));
    }
}
