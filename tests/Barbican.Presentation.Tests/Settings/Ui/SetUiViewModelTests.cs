// Not Rust rows: I-395 (rows with their values), I-396 (search), I-397 (a hit opens its page), I-398 (Show advanced settings), D-M6-00 (Changed arrives on a pool thread and is posted to the dispatcher before PropertyChanged). M6 stream set-ui.

using System.Text.Json.Nodes;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Ui;

namespace Barbican.Presentation.Tests.Settings.Ui;

public sealed class SetUiViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class QueueDispatcher : IDispatcher
    {
        public Queue<Action> Posted { get; } = new();

        public bool HasThreadAccess => false;

        public void Post(Action action) => Posted.Enqueue(action);

        public Task InvokeAsync(Func<Task> action) => action();
    }

    private static SetUiViewModel Create(SampleSettingValues? values = null, IDispatcher? dispatcher = null) =>
        new(values ?? new SampleSettingValues(), new SampleSettingInfo(), new SettingsNavigation(), null, dispatcher);

    [Fact]
    public void The_schema_gives_21_pages_with_the_decided_row_ids()
    {
        var pages = SetUiCatalog.FromSchema();
        Assert.Equal(21, pages.Count);
        Assert.Equal(277, pages.Sum(p => p.Rows.Count));
        Assert.Contains(pages.SelectMany(p => p.Rows), r => string.Equals(r.Id, "S-uiStyle", StringComparison.Ordinal));
        Assert.Contains(pages.SelectMany(p => p.Rows), r => string.Equals(r.Id, "S-keyless-001", StringComparison.Ordinal));
    }

    [Fact]
    public void Pages_of_Later_and_advanced_pages_hide_until_Show_advanced()
    {
        var vm = Create();
        Assert.DoesNotContain(vm.VisiblePages, p => string.Equals(p.Id, SettingsPageIds.Addons, StringComparison.Ordinal));
        Assert.DoesNotContain(vm.VisiblePages, p => string.Equals(p.Id, SettingsPageIds.Troubleshooting, StringComparison.Ordinal));
        vm.ShowAdvanced = true;
        Assert.Contains(vm.VisiblePages, p => string.Equals(p.Id, SettingsPageIds.Troubleshooting, StringComparison.Ordinal));
    }

    [Fact]
    public void Rows_carry_the_default_until_a_value_is_stored()
    {
        var vm = Create();
        var row = vm.RowsOf(SettingsPageIds.Appearance).Single(r => string.Equals(r.Row.Id, "S-uiStyle", StringComparison.Ordinal));
        Assert.Equal("bulwark", (string?)row.Value);
    }

    [Fact]
    public async Task A_written_value_comes_back_on_the_next_read_and_PropertyChanged_runs_on_the_dispatcher()
    {
        var dispatcher = new QueueDispatcher();
        var vm = Create(dispatcher: dispatcher);
        var raised = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, "Values", StringComparison.Ordinal))
            {
                raised++;
            }
        };
        await vm.SetValueAsync("S-uiStyle", JsonValue.Create("nordic"), Ct);
        Assert.Equal("nordic", (string?)vm.RowsOf(SettingsPageIds.Appearance).Single(r => string.Equals(r.Row.Id, "S-uiStyle", StringComparison.Ordinal)).Value);
        // the fake raised Changed from the pool: the view model queued it instead of raising on that thread
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dispatcher.Posted.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Ct);
        }

        Assert.NotEmpty(dispatcher.Posted);
        while (dispatcher.Posted.Count > 0)
        {
            dispatcher.Posted.Dequeue()();
        }

        Assert.True(raised >= 1);
    }

    [Fact]
    public void A_keyless_theme_row_writes_the_theme_pref()
    {
        var row = SetUiCatalog.FromSchema().SelectMany(p => p.Rows).Single(r => string.Equals(r.Id, "S-keyless-025", StringComparison.Ordinal));
        Assert.Equal("theme", SetUiRowRules.KeyOf(row));
    }

    [Fact]
    public async Task Files_controls_preserve_other_fields_and_reject_invalid_choices()
    {
        var vm = Create();
        Assert.Equal("list", (string?)vm.RowsOf(SettingsPageIds.Files).Single(r => string.Equals(r.Row.Id, "S-keyless-054", StringComparison.Ordinal)).Value);
        Assert.True((bool?)vm.RowsOf(SettingsPageIds.Files).Single(r => string.Equals(r.Row.Id, "S-keyless-057", StringComparison.Ordinal)).Value);
        await Task.WhenAll(
            vm.SetValueAsync("S-keyless-054", JsonValue.Create("grid"), Ct),
            vm.SetValueAsync("S-keyless-060", JsonValue.Create(true), Ct));
        var prefs = Assert.IsType<JsonObject>(vm.ValueOf("filesSettings"));
        Assert.Equal("grid", (string?)prefs["defaultViewMode"]);
        Assert.True((bool?)prefs["showHiddenFiles"]);
        await vm.SetValueAsync("S-keyless-054", JsonValue.Create("invalid"), Ct);
        Assert.NotEmpty(vm.LastError);
        Assert.Equal("grid", (string?)vm.ValueOf("filesSettings")!["defaultViewMode"]);
    }

    [Fact]
    public async Task Working_days_keep_concurrent_changes_and_refuse_an_empty_selection()
    {
        var vm = Create();
        await Task.WhenAll(vm.SetWorkingDayAsync(0, true, Ct), vm.SetWorkingDayAsync(1, false, Ct));
        var days = Assert.IsType<JsonArray>(vm.ValueOf("calendarWorkingDays"));
        Assert.Equal("0,2,3,4,5", string.Join(',', days.Select(x => (int)x!)));
        foreach (var day in new[] { 0, 2, 3, 4, 5 }) { await vm.SetWorkingDayAsync(day, false, Ct); }
        Assert.Equal("Choose at least one working day.", vm.LastError);
        Assert.Single(Assert.IsType<JsonArray>(vm.ValueOf("calendarWorkingDays")));
    }

    [Fact]
    public async Task Account_marker_selects_two_boolean_preferences()
    {
        var vm = Create();
        var row = vm.RowsOf(SettingsPageIds.Appearance).Single(r => string.Equals(r.Row.Key, "tintListRowsByAccount", StringComparison.Ordinal));
        Assert.Equal("dot", (string?)row.Value);
        await vm.SetValueAsync(row.Row.Id, JsonValue.Create("tint"), Ct);
        Assert.True((bool?)vm.ValueOf("tintListRowsByAccount"));
        Assert.False((bool?)vm.ValueOf("showAccountDot"));
        await vm.SetValueAsync(row.Row.Id, JsonValue.Create("none"), Ct);
        Assert.False((bool?)vm.ValueOf("tintListRowsByAccount"));
        Assert.False((bool?)vm.ValueOf("showAccountDot"));
    }

    [Fact]
    public async Task Calendar_hours_keep_the_end_after_the_start()
    {
        var vm = Create();
        await vm.SetCalendarHourAsync(true, 22, Ct);
        Assert.Equal(22, (int?)vm.ValueOf("calendarDayStartHour"));
        Assert.Equal(23, (int?)vm.ValueOf("calendarDayEndHour"));
        await vm.SetCalendarHourAsync(false, 5, Ct);
        Assert.Equal(4, (int?)vm.ValueOf("calendarDayStartHour"));
        Assert.Equal(5, (int?)vm.ValueOf("calendarDayEndHour"));
    }

    [Fact]
    public async Task Calendar_default_view_validates_keyless_choices()
    {
        var vm = Create();
        await vm.SetValueAsync("S-keyless-049", JsonValue.Create("agenda"), Ct);
        Assert.Equal("agenda", (string?)vm.ValueOf("calendarViewMode"));
        await vm.SetValueAsync("S-keyless-049", JsonValue.Create("invalid"), Ct);
        Assert.NotEmpty(vm.LastError);
        Assert.Equal("agenda", (string?)vm.ValueOf("calendarViewMode"));
    }

    [Fact]
    public async Task Order_scope_toggle_stores_a_scope_string()
    {
        var vm = Create();
        await vm.SetValueAsync("S-messageListOrderScope", JsonValue.Create(true), Ct);
        Assert.Equal("all", (string?)vm.ValueOf("messageListOrderScope"));
        await vm.SetValueAsync("S-messageListOrderScope", JsonValue.Create(false), Ct);
        Assert.Equal("inbox", (string?)vm.ValueOf("messageListOrderScope"));
    }

    [Fact]
    public void Search_finds_rows_by_label_and_clears()
    {
        var vm = Create();
        vm.SearchText = "density";
        Assert.Contains(vm.SearchHits, h => string.Equals(h.RowId, "S-density", StringComparison.Ordinal) && string.Equals(h.PageId, SettingsPageIds.Appearance, StringComparison.Ordinal));
        vm.SearchText = string.Empty;
        Assert.Empty(vm.SearchHits);
    }

    [Fact]
    public void An_inverted_toggle_shows_the_opposite_of_its_key()
    {
        var row = SetUiCatalog.FromSchema().SelectMany(p => p.Rows).Single(r => string.Equals(r.Id, "S-disableThreading", StringComparison.Ordinal));
        Assert.True(SetUiRowRules.ToggleOn(row, null));
        Assert.True(SetUiRowRules.ToggleOn(row, JsonValue.Create(false)));
        Assert.False(SetUiRowRules.ToggleOn(row, JsonValue.Create(true)));
        Assert.True(SetUiRowRules.ToStored(row, shownOn: false));
    }

    [Fact]
    public void Byte_and_quota_text_read_like_the_real_one()
    {
        Assert.Equal("3.5 MB of 1 GB used", SetUiFormat.Quota((long)(3.5 * 1024 * 1024), 1024L * 1024 * 1024));
        Assert.Equal("512 B used", SetUiFormat.Quota(512, null));
        Assert.Equal(0.5, SetUiFormat.QuotaFraction(5, 10));
    }
}
