// New tests (M4 plan 6.3 acceptance): the range of every view, paging by the view's unit, Today, the month boundary, first day of the week, hidden non-working days, selection versus opening, calendar visibility, search, the load error and Retry, the five hooks. Owner cal-views.

#pragma warning disable MA0002, MA0006 // ids and titles are compared ordinally by nature
using Barbican.Core.Calendar;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Calendar.Grid;
using static Barbican.Presentation.Tests.Calendar.Grid.CalendarHarness;

namespace Barbican.Presentation.Tests.Calendar.Grid;

public sealed class CalendarViewModelTests
{
    [Fact]
    public async Task Choosing_views_persists_the_last_choice_in_order()
    {
        using var h = new CalendarHarness();
        var values = new Barbican.Presentation.Settings.Sample.SampleSettingValues();
        using var vm = new CalendarViewModel(h.Source, h.Accounts, h.Prefs, h.Bus, h.Time, new Barbican.Presentation.Common.InlineDispatcher(), null, values);
        vm.SetView(CalendarViewKind.Day);
        vm.SetView(CalendarViewKind.Month);
        vm.SetView(CalendarViewKind.Agenda);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!string.Equals((string?)values.Stored("calendarViewMode"), "agenda", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal("agenda", (string?)values.Stored("calendarViewMode"));
    }

    [Fact]
    public void Saved_view_is_used_and_external_changes_update_calendar()
    {
        using var initial = new CalendarHarness("{}");
        Assert.Equal(CalendarViewKind.Month, initial.View.View);
        using var h = new CalendarHarness("{\"calendarViewMode\":\"agenda\"}");
        Assert.Equal(CalendarViewKind.Agenda, h.View.View);
        h.Prefs.Set("{\"calendarViewMode\":\"day\"}");
        Assert.Equal(CalendarViewKind.Day, h.View.View);
        h.Prefs.Set("{\"calendarViewMode\":\"unknown\"}");
        Assert.Equal(CalendarViewKind.Month, h.View.View);
    }

    [Fact]
    public void Every_view_has_its_range()
    {
        using var h = new CalendarHarness();
        var v = h.View;
        v.SetView(CalendarViewKind.Day);
        Assert.Equal((D("2026-11-18"), D("2026-11-19")), v.Range);
        v.SetView(CalendarViewKind.Week);
        Assert.Equal((D("2026-11-16"), D("2026-11-23")), v.Range);
        v.SetView(CalendarViewKind.Month);
        Assert.Equal((D("2026-10-26"), D("2026-12-07")), v.Range);
        Assert.Equal(42, v.Range.To.DayNumber - v.Range.From.DayNumber);
        v.SetView(CalendarViewKind.Agenda);
        Assert.Equal((D("2026-11-18"), D("2026-12-18")), v.Range);
        Assert.Equal("Nov 18 – Dec 17, 2026", v.Title);
    }

    [Fact]
    public void Paging_moves_by_the_views_unit_and_today_comes_back()
    {
        using var h = new CalendarHarness();
        var v = h.View;
        v.SetView(CalendarViewKind.Day);
        v.GoNext();
        Assert.Equal(D("2026-11-19"), v.Anchor);
        v.GoPrevious();
        v.GoPrevious();
        Assert.Equal(D("2026-11-17"), v.Anchor);
        v.SetView(CalendarViewKind.Week);
        v.GoNext();
        Assert.Equal(D("2026-11-24"), v.Anchor);
        Assert.Equal(D("2026-11-23"), v.Range.From);
        v.SetView(CalendarViewKind.Agenda);
        v.GoNext();
        Assert.Equal(D("2026-12-24"), v.Anchor);
        v.Today();
        Assert.Equal(D("2026-11-18"), v.Anchor);
    }

    [Fact]
    public void Month_paging_lands_on_the_first_of_the_month_across_the_boundary()
    {
        using var h = new CalendarHarness();
        var v = h.View;
        v.SetView(CalendarViewKind.Month);
        v.ShowDay(D("2027-01-31"));
        v.GoNext();
        Assert.Equal(D("2027-02-01"), v.Anchor);
        Assert.Equal("February 2027", v.Title);
        v.GoPrevious();
        v.GoPrevious();
        Assert.Equal(D("2026-12-01"), v.Anchor);
        v.ShowDay(D("2026-12-31"));
        v.GoNext();
        Assert.Equal(D("2027-01-01"), v.Anchor);
    }

    [Fact]
    public void The_first_day_of_the_week_follows_the_setting()
    {
        using var h = new CalendarHarness("""{"firstDayOfWeek": 0}""");
        h.View.SetView(CalendarViewKind.Week);
        Assert.Equal((D("2026-11-15"), D("2026-11-22")), h.View.Range);
        h.View.SetView(CalendarViewKind.Month);
        Assert.Equal(D("2026-11-01"), h.View.Range.From);
        h.Prefs.Set("""{"firstDayOfWeek": 1}""");
        Assert.Equal(D("2026-10-26"), h.View.Range.From);
    }

    [Fact]
    public void A_narrow_pane_shows_three_days_unless_all_days_are_asked_for()
    {
        using var h = new CalendarHarness();
        h.View.SetView(CalendarViewKind.Week);
        h.View.SetNarrow(true);
        Assert.Equal((D("2026-11-18"), D("2026-11-21")), h.View.Range);
        Assert.Equal(3, h.View.Columns.Count);
        h.View.GoNext();
        Assert.Equal(D("2026-11-21"), h.View.Anchor);
        h.Prefs.Set("""{"calendarNarrowWeekAllDays": true, "calendarViewMode": "week"}""");
        Assert.Equal(7, h.View.Columns.Count);
    }

    [Fact]
    public void Non_working_days_can_be_hidden_in_the_week()
    {
        using var h = new CalendarHarness("""{"calendarHideNonWorkingDays": true}""");
        h.View.SetView(CalendarViewKind.Week);
        Assert.Equal(5, h.View.Columns.Count);
        Assert.Equal(2, h.View.HiddenDayCount);
        Assert.DoesNotContain(h.View.Columns, d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    [Fact]
    public async Task Occurrences_follow_the_range_and_the_visible_calendars()
    {
        using var h = new CalendarHarness();
        h.Add("a", "Stand-up", "2026-11-18T10:30", "2026-11-18T11:00");
        h.Add("b", "Dinner", "2026-11-19T19:00", "2026-11-19T21:00", calendar: "home");
        h.Add("c", "Next month", "2026-12-20T10:00", "2026-12-20T11:00");
        await h.ReloadAsync();
        h.View.SetView(CalendarViewKind.Week);
        Assert.Equal(["a", "b"], h.View.Occurrences.Select(o => o.Id));
        h.View.ToggleCalendar("home");
        Assert.Equal(["a"], h.View.Occurrences.Select(o => o.Id));
        Assert.False(h.View.Calendars.Single(c => c.Id == "home").Visible);
        h.View.ToggleCalendar("home");
        Assert.Equal(2, h.View.Occurrences.Count);
        h.View.GoNext();
        Assert.Empty(h.View.Occurrences);
    }

    [Fact]
    public async Task Tasks_and_birthdays_show_only_when_their_settings_are_on()
    {
        using var h = new CalendarHarness("""{"showTasksOnCalendar": false}""");
        h.Add("task:1", "○ Pay rent", "2026-11-18T00:00", "2026-11-19T00:00", allDay: true, kind: CalendarOccurrenceKind.Task);
        h.Add("bday:1", "Ann", "2026-11-18T00:00", "2026-11-19T00:00", allDay: true, kind: CalendarOccurrenceKind.Birthday);
        h.Add("e", "Meeting", "2026-11-18T09:00", "2026-11-18T10:00");
        await h.ReloadAsync();
        Assert.Equal(["e"], h.View.Occurrences.Select(o => o.Id));
        h.Prefs.Set("""{"showTasksOnCalendar": true, "showBirthdayCalendar": true}""");
        Assert.Equal(3, h.View.Occurrences.Count);
    }

    [Fact]
    public async Task Selecting_is_not_opening()
    {
        using var h = new CalendarHarness();
        h.Add("a", "Stand-up", "2026-11-18T10:30", "2026-11-18T11:00");
        await h.ReloadAsync();
        h.View.SelectOccurrence("a");
        Assert.Equal("a", h.View.SelectedId);
        Assert.Equal("Stand-up", h.View.Detail?.Title);
        Assert.Empty(h.Editors.Opened);
        await h.View.OpenAsync("a", CancellationToken.None);
        Assert.Equal(["a"], h.Editors.Opened);
    }

    [Fact]
    public async Task A_selection_that_leaves_the_screen_is_dropped()
    {
        using var h = new CalendarHarness();
        h.Add("a", "Stand-up", "2026-11-18T10:30", "2026-11-18T11:00");
        await h.ReloadAsync();
        h.View.SelectOccurrence("a");
        h.View.GoNext();
        Assert.Null(h.View.SelectedId);
    }

    [Fact]
    public async Task Search_filters_the_grid_and_lists_results_across_the_data()
    {
        using var h = new CalendarHarness();
        h.Add("a", "Stand-up", "2026-11-18T10:30", "2026-11-18T11:00");
        h.Add("b", "Dentist", "2026-11-19T09:00", "2026-11-19T10:00");
        h.Add("c", "Dentist follow-up", "2027-03-02T09:00", "2027-03-02T10:00");
        await h.ReloadAsync();
        h.View.SearchText = "dent";
        Assert.Equal(["b"], h.View.Occurrences.Select(o => o.Id));
        Assert.Equal(["b", "c"], h.View.SearchResults.Select(o => o.Id));
        h.View.ShowOccurrence(h.View.SearchResults[1]);
        Assert.Equal(D("2027-03-02"), h.View.Anchor);
        Assert.Equal("c", h.View.SelectedId);
        h.View.SearchText = string.Empty;
        Assert.Empty(h.View.SearchResults);
    }

    [Fact]
    public async Task A_load_error_shows_and_retry_clears_it()
    {
        using var h = new CalendarHarness();
        h.Source.LoadResult = Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Fail(new TransportError("server unreachable", true));
        await h.ReloadAsync();
        Assert.Equal("server unreachable", h.View.LoadError);
        Assert.False(h.View.IsLoading);
        h.Source.LoadResult = Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Ok(default);
        await h.View.LoadAsync(CancellationToken.None);
        Assert.Null(h.View.LoadError);
        Assert.Equal(2, h.Source.Loads);
    }

    [Fact]
    public async Task A_change_in_the_source_redraws_without_a_reload()
    {
        using var h = new CalendarHarness();
        await h.ReloadAsync();
        Assert.Empty(h.View.Occurrences);
        h.Add("a", "Stand-up", "2026-11-18T10:30", "2026-11-18T11:00");
        h.Source.Raise();
        Assert.Single(h.View.Occurrences);
        Assert.Equal(1, h.Source.Loads);
    }

    [Fact]
    public async Task The_mini_month_marks_busy_days_and_the_shown_week()
    {
        using var h = new CalendarHarness();
        h.Add("a", "Stand-up", "2026-11-18T10:30", "2026-11-18T11:00");
        await h.ReloadAsync();
        var days = h.View.MiniDays;
        Assert.Equal(42, days.Count);
        Assert.True(days.Single(d => d.Day == D("2026-11-18")).Busy);
        Assert.False(days.Single(d => d.Day == D("2026-11-17")).Busy);
        Assert.True(days.Single(d => d.Day == D("2026-11-18")).Today);
        Assert.Equal(7, days.Count(d => d.Selected));
        Assert.False(days.Single(d => d.Day == D("2026-10-26")).InMonth);
        h.View.MiniStep(1);
        Assert.Equal(D("2026-12-01"), h.View.MiniMonth);
        Assert.Equal(D("2026-11-18"), h.View.Anchor);
        h.View.SetMiniMonth(2027, 6);
        Assert.Equal(D("2027-06-01"), h.View.MiniMonth);
        Assert.Equal(6, h.View.MiniWeekNumbers.Count);
    }

    [Fact]
    public void The_task_day_filter_follows_the_mini_month_in_the_tasks_view()
    {
        using var h = new CalendarHarness();
        h.View.SetView(CalendarViewKind.Tasks);
        h.View.ToggleTaskDay("2026-11-19");
        Assert.Equal(D("2026-11-19"), h.View.TaskDay.Day);
        Assert.Single(h.View.MiniDays, d => d.Selected);
        h.View.ToggleTaskDay("clear");
        Assert.Null(h.View.TaskDay.Day);
        Assert.Equal("Tasks", h.View.Title);
    }

    [Fact]
    public void The_titles_follow_the_view()
    {
        using var h = new CalendarHarness("""{"showWeekNumbers": true}""");
        h.View.SetView(CalendarViewKind.Day);
        Assert.Equal("Wednesday 18 November  ·  Week 47", h.View.Title);
        h.View.SetView(CalendarViewKind.Week);
        Assert.Equal("16 – 22 November", h.View.Title);
        h.View.ShowDay(D("2026-11-30"));
        Assert.Equal("30 November – 6 December", h.View.Title);
        h.View.SetView(CalendarViewKind.Month);
        Assert.Equal("November 2026", h.View.Title);
        Assert.Equal("Nov 2026", h.View.ShortTitle);
    }

    [Fact]
    public void The_tasks_view_is_offered_only_when_tasks_are_on_and_the_tasks_app_is_not_separate()
    {
        using var h = new CalendarHarness();
        Assert.Contains(CalendarViewKind.Tasks, h.View.AvailableViews);
        h.Prefs.Set("""{"showTasksApp": true}""");
        Assert.DoesNotContain(CalendarViewKind.Tasks, h.View.AvailableViews);
        h.Prefs.Set("""{"enableCalendarTasks": false}""");
        Assert.DoesNotContain(CalendarViewKind.Tasks, h.View.AvailableViews);
    }

    [Fact]
    public async Task The_five_hooks_fire_with_their_payloads()
    {
        using var h = new CalendarHarness();
        var dates = new List<CalendarDatePayload>();
        var views = new List<CalendarViewPayload>();
        var toggles = new List<CalendarVisibilityPayload>();
        var changes = new List<CalendarChangePayload>();
        var opens = new List<CalendarEventOpenPayload>();
        using var s1 = h.Bus.Observe<CalendarDatePayload>(HookNames.OnCalendarDateChange, (p, _) => { lock (dates) { dates.Add(p); } return ValueTask.CompletedTask; });
        using var s2 = h.Bus.Observe<CalendarViewPayload>(HookNames.OnCalendarViewChange, (p, _) => { lock (views) { views.Add(p); } return ValueTask.CompletedTask; });
        using var s3 = h.Bus.Observe<CalendarVisibilityPayload>(HookNames.OnCalendarVisibilityToggle, (p, _) => { lock (toggles) { toggles.Add(p); } return ValueTask.CompletedTask; });
        using var s4 = h.Bus.Observe<CalendarChangePayload>(HookNames.OnCalendarChange, (p, _) => { lock (changes) { changes.Add(p); } return ValueTask.CompletedTask; });
        using var s5 = h.Bus.Observe<CalendarEventOpenPayload>(HookNames.OnCalendarEventOpen, (p, _) => { lock (opens) { opens.Add(p); } return ValueTask.CompletedTask; });
        h.Add("a", "Stand-up", "2026-11-18T10:30", "2026-11-18T11:00");
        await h.ReloadAsync();
        await h.View.OpenAsync("a", CancellationToken.None);
        h.View.SetView(CalendarViewKind.Month);
        h.View.GoNext();
        h.View.ToggleCalendar("home");
        h.Source.Rows.Add(new("new", "acc", "New", "#ef4444", true, false, false, false, true));
        h.Source.Raise(calendars: true);
        await EventuallyAsync(() => dates.Count == 1 && views.Count == 1 && toggles.Count == 1 && changes.Count == 1 && opens.Count == 1);
        Assert.Equal("2026-12-01", dates[0].Date);
        Assert.Equal("month", views[0].View);
        Assert.Equal(new CalendarVisibilityPayload("home", false), toggles[0]);
        Assert.Equal(new CalendarChangePayload("new", "created"), changes[0]);
        Assert.Equal("Stand-up", opens[0].Title);
    }
}
