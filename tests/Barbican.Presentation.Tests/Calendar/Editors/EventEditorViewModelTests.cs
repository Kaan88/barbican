// M4 stream cal-ui acceptance: the editor's save builds the right EventForm for a new, a timed, an all-day, a recurring and a guest event; the dirty check; the scope flow for edit and delete; alert rows; Find a time (M4 plan 6.4).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Presentation.Calendar.Editors;

namespace Barbican.Presentation.Tests.Calendar.Editors;

public sealed class EventEditorViewModelTests
{
    private readonly FakeSource _source = new();
    private readonly FakeWriter _writer = new();
    private readonly FakeRules _rules = new();
    private readonly FakeQuestions _questions = new();

    private EventEditorViewModel Editor(Barbican.Core.Calendar.Editing.IAvailabilityService? availability = null) =>
        new(new EventEditorServices(_source, _writer, _rules, _questions, availability, null, null, null, null, false));

    private static DateTime Dt(string s) => DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private static JsonNode Series() => JsonNode.Parse("""
        {"id":"e1","uid":"u1","title":"Standup","start":"2026-11-16T09:00:00","duration":"PT30M","timeZone":"Europe/Berlin",
         "calendarIds":{"work":true},"recurrenceRules":[{"@type":"RecurrenceRule","frequency":"daily"}],"useDefaultAlerts":false,
         "alerts":{"a1":{"trigger":{"offset":"-PT15M"}}}}
        """)!;

    [Fact]
    public async Task A_new_timed_event_builds_the_form_and_writes_a_create()
    {
        var e = Editor();
        e.OpenNew(new NewEventRequest(Dt("2026-11-20T14:00:00"), Dt("2026-11-20T15:30:00"), false));
        e.Title = " Review ";
        Assert.True(await e.SaveAsync(CancellationToken.None));
        var f = _rules.LastForm!;
        Assert.Equal(Dt("2026-11-20T14:00:00"), f.Start);
        Assert.Equal(Dt("2026-11-20T15:30:00"), f.End);
        Assert.False(f.AllDay);
        Assert.Equal("work", f.Calendar);
        Assert.Equal(0, f.Repeat);
        Assert.True(f.UseDefaultAlerts);
        var w = Assert.Single(_writer.Saves);
        Assert.Null(w.EventId);
        Assert.Equal(EditScope.WholeEvent, w.Scope);
        Assert.Equal("Review", (string?)w.Data["title"]);
        Assert.True(e.Closed.IsCompleted);
    }

    [Fact]
    public void A_new_all_day_event_has_no_zone_and_the_last_day()
    {
        var e = Editor();
        e.OpenNew(new NewEventRequest(Dt("2026-11-20T00:00:00"), Dt("2026-11-22T00:00:00"), true));
        var f = e.BuildForm();
        Assert.True(f.AllDay);
        Assert.Null(f.TimeZone);
        Assert.Equal(Dt("2026-11-22T00:00:00"), f.End);
        Assert.Equal(1, f.FreeBusy);
    }

    [Fact]
    public void A_custom_recurrence_is_in_the_form_with_the_rule_of_the_editor()
    {
        var e = Editor();
        e.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        e.Repeat = 5;
        e.Recurrence.Interval = "2";
        var f = e.BuildForm();
        Assert.Equal(5, f.Repeat);
        Assert.Equal("weekly", f.Custom!.Freq);
        Assert.Equal(2, f.Custom.Interval);
        Assert.Equal([new ByDayEntry(DayOfWeek.Friday)], f.Custom.ByDay);
        Assert.Equal("weekly/2/Friday", e.RepeatSummary);
    }

    [Fact]
    public void Guests_are_chips_and_a_bad_address_is_refused()
    {
        var e = Editor();
        e.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        e.GuestInput = "Ann Lee <ann@y.example>";
        Assert.True(e.CommitGuest());
        e.GuestInput = "not an address";
        Assert.False(e.CommitGuest());
        Assert.Contains("not an email", e.Error, StringComparison.Ordinal);
        e.GuestInput = "ANN@y.example";
        e.CommitGuest();
        e.GuestInput = "me@x.example";
        e.CommitGuest();
        var f = e.BuildForm();
        Assert.Equal([new CalendarPerson("Ann Lee", "ann@y.example")], f.Attendees);
        Assert.True(f.Send);
    }

    [Fact]
    public async Task Saving_one_occurrence_asks_the_scope_and_writes_the_recurrence_id()
    {
        _questions.Scopes.Enqueue((EditScope.Occurrence, false));
        var e = Editor();
        e.OpenExisting(Series(), "2026-11-18T09:00:00");
        e.Title = "Standup (moved)";
        Assert.True(await e.SaveAsync(CancellationToken.None));
        var w = Assert.Single(_writer.Saves);
        Assert.Equal(EditScope.Occurrence, w.Scope);
        Assert.Equal("2026-11-18T09:00:00", w.RecurrenceId);
        Assert.Equal("e1", w.EventId);
        Assert.Equal(Dt("2026-11-18T09:00:00"), _rules.LastOccurrence!.Value.Start);
        Assert.Contains("scope", _questions.Asked, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(EditScope.Following)]
    [InlineData(EditScope.All)]
    public async Task Saving_following_or_all_passes_that_scope(EditScope scope)
    {
        _questions.Scopes.Enqueue((scope, false));
        var e = Editor();
        e.OpenExisting(Series(), "2026-11-18T09:00:00");
        e.Title = "Changed";
        Assert.True(await e.SaveAsync(CancellationToken.None));
        Assert.Equal(scope, _writer.Saves[0].Scope);
    }

    [Fact]
    public async Task Cancelling_the_scope_question_writes_nothing_and_keeps_the_editor()
    {
        var e = Editor();
        e.OpenExisting(Series(), "2026-11-18T09:00:00");
        e.Title = "Changed";
        Assert.False(await e.SaveAsync(CancellationToken.None));
        Assert.Empty(_writer.Saves);
        Assert.False(e.Closed.IsCompleted);
    }

    [Fact]
    public async Task A_failed_write_keeps_the_editor_open_with_the_error()
    {
        _writer.Fail = new Barbican.Core.Jmap.Errors.ValidationError("Server said no");
        var e = Editor();
        e.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        Assert.False(await e.SaveAsync(CancellationToken.None));
        Assert.Equal("Server said no", e.Error);
        Assert.False(e.Closed.IsCompleted);
    }

    [Fact]
    public async Task The_dirty_check_asks_only_after_a_change_and_discard_closes()
    {
        var e = Editor();
        e.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        Assert.False(e.IsDirty);
        Assert.True(await e.RequestCloseAsync(CancellationToken.None));
        Assert.Empty(_questions.Asked);

        var d = Editor();
        d.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        d.Title = "x";
        Assert.True(d.IsDirty);
        _questions.Answers.Enqueue("keep");
        Assert.False(await d.RequestCloseAsync(CancellationToken.None));
        _questions.Answers.Enqueue("discard");
        Assert.True(await d.RequestCloseAsync(CancellationToken.None));
        Assert.Equal(["discard", "discard"], _questions.Asked);
    }

    [Fact]
    public async Task Deleting_a_series_asks_the_scope_and_notify_and_deleting_a_single_event_confirms()
    {
        _questions.Scopes.Enqueue((EditScope.Following, true));
        var e = Editor();
        e.OpenExisting(Series(), "2026-11-18T09:00:00");
        Assert.True(await e.DeleteAsync(CancellationToken.None));
        Assert.Equal(("e1", EditScope.Following, "2026-11-18T09:00:00", true), _writer.Deletes[0]);

        var single = JsonNode.Parse("""{"id":"s1","title":"Lunch","start":"2026-11-20T12:00:00","duration":"PT1H","calendarIds":{"work":true}}""")!;
        var d = Editor();
        d.OpenExisting(single, null);
        _questions.Answers.Enqueue("cancel");
        Assert.False(await d.DeleteAsync(CancellationToken.None));
        _questions.Answers.Enqueue("quiet");
        Assert.True(await d.DeleteAsync(CancellationToken.None));
        Assert.Equal(("s1", EditScope.WholeEvent, null, false), _writer.Deletes[1]);
    }

    [Fact]
    public void Alert_rows_round_trip_and_customise_copies_the_defaults()
    {
        var e = Editor();
        e.OpenExisting(Series(), null);
        Assert.False(e.UseDefaultAlerts);
        var row = Assert.Single(e.Alerts);
        Assert.Equal(15, row.Value);
        Assert.Equal("a1", row.Source!.Id);
        Assert.Equal(["a1"], e.BuildForm().ReservedAlertIds);
        Assert.Equal(15, e.BuildForm().Rows[0].Row.Value);

        var n = Editor();
        n.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        Assert.True(n.UseDefaultAlerts);
        Assert.Equal("10 minutes before", n.DefaultAlertsLabel);
        n.AddAlert();
        Assert.False(n.UseDefaultAlerts);
        Assert.Single(n.Alerts);
        n.UseDefaults();
        n.CustomizeAlerts();
        Assert.Equal(10, Assert.Single(n.Alerts).Value);
    }

    [Fact]
    public void An_event_the_user_does_not_organise_opens_in_view_mode()
    {
        var ev = JsonNode.Parse("""
            {"id":"i1","title":"Board","start":"2026-11-20T12:00:00","duration":"PT1H","calendarIds":{"work":true},
             "organizerCalendarAddress":"mailto:ann@y.example","participants":{"p1":{"email":"ann@y.example","roles":{"owner":true}},"p2":{"email":"me@x.example","name":"Me"}}}
            """)!;
        var e = Editor();
        e.OpenExisting(ev, null);
        Assert.True(e.ReadOnly);
        Assert.Equal("Event", e.Heading);
        Assert.Equal("Organised by Ann Lee", e.OrganizerLine);
    }

    [Fact]
    public void An_event_in_a_feed_calendar_is_read_only()
    {
        var ev = JsonNode.Parse("""{"id":"f1","title":"Holiday","start":"2026-12-25T00:00:00","duration":"P1D","showWithoutTime":true,"calendarIds":{"feed":true}}""")!;
        var e = Editor();
        e.OpenExisting(ev, null);
        Assert.True(e.ReadOnly);
    }

    [Fact]
    public void A_web_address_as_location_is_the_meeting_link()
    {
        var e = Editor();
        e.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        Assert.Null(e.MeetingLink);
        e.Location = "https://meet.example/abc";
        Assert.Equal("https://meet.example/abc", e.MeetingLink);
        e.Location = "Room 4";
        Assert.Null(e.MeetingLink);
    }

    [Fact]
    public async Task Find_a_time_is_hidden_without_the_capability_and_shows_at_most_five_with_it()
    {
        var e = Editor();
        Assert.False(e.AvailabilitySupported);

        var service = new FakeAvailability(true);
        var f = Editor(service);
        f.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        f.GuestInput = "bob@y.example";
        f.CommitGuest();
        Assert.True(f.AvailabilitySupported);
        _rules.Slots = [.. Enumerable.Range(0, 8).Select(i => new FreeSlot(Dt("2026-11-23T09:00:00").AddHours(i), Dt("2026-11-23T10:00:00").AddHours(i)))];
        await f.FindATimeAsync(CancellationToken.None);
        Assert.Equal(5, f.Slots.Count);
        f.ApplySlot(f.Slots[1]);
        Assert.Equal("10:00", f.StartTime);
        Assert.Equal(new DateOnly(2026, 11, 23), f.StartDate);

        var off = Editor(new FakeAvailability(false));
        off.OpenNew(new NewEventRequest(Dt("2026-11-20T10:00:00"), Dt("2026-11-20T11:00:00"), false));
        Assert.False(off.AvailabilitySupported);
    }

    [Fact]
    public void The_footer_duplicate_is_a_new_event_with_the_same_fields()
    {
        var e = Editor();
        e.OpenExisting(Series(), null);
        var copy = e.DuplicateCopy();
        Assert.True(copy.IsNew);
        Assert.Equal("Standup", copy.Title);
        Assert.Equal(1, copy.Repeat);
        Assert.Equal("work", copy.CalendarId);
    }
}
