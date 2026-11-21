// Source: Bulwark lib/plugin-sandbox/open-email.ts, calendar-navigation.ts, open-task.ts, row-badges.ts, host-api.ts:1371-1411,2118-2127,2310-2349 (+ lib/__tests__/plugin-open-email, -open-task, -calendar-navigation, -row-badges tests);
// Barbican fences (denial with a neighbouring permission, stale login or grant, foreign account, limits, no stacked dialogs) over FakeJmap and recording doubles for the app seams.
using Barbican.Core.Plugins.Api.Navigation;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginNavigationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Msg(string id) => "\"" + id + "\"";

    [Theory]
    [InlineData("ui.openEmail", """[{"id":"x"}]""", "email:write")]
    [InlineData("ui.openCalendar", """[{"view":"month"}]""", "ui:tasks")]
    [InlineData("ui.openCalendar", """[{"view":"tasks"}]""", "calendar:read")]
    [InlineData("ui.openTask", """["t"]""", "calendar:read")]
    [InlineData("ui.downloadFile", """[{"content":"x","filename":"a.txt"}]""", "ui:tasks")]
    public async Task Every_method_is_denied_with_a_neighbouring_permission_and_moves_nothing(string method, string args, string held)
    {
        var rig = await PluginNavigationRig.StartAsync([held]);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, args)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task OpenEmail_reads_the_message_through_the_login_then_opens_it_in_the_reader()
    {
        var rig = await PluginNavigationRig.StartAsync(["email:read"]);
        var id = rig.IdOf("Flagged reminder");
        var result = await rig.CallAsync("ui.openEmail", $$"""[{"id":{{Msg(id)}}}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Undefined, result.Value!.Kind);
        var (account, shared, summary) = Assert.Single(rig.Nav.Emails);
        Assert.Equal(rig.Api.Account.Key, account);
        Assert.Null(shared);
        Assert.Equal(id, summary.Id);
        Assert.Equal("Flagged reminder", summary.Subject);
    }

    [Theory]
    [InlineData("""[{"id":"nope"}]""")]
    [InlineData("""[{"id":"x","accountId":"someone-elses"}]""")]
    public async Task OpenEmail_refuses_a_message_the_login_cannot_read_and_an_account_it_does_not_show(string args)
    {
        var rig = await PluginNavigationRig.StartAsync(["email:read"]);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("ui.openEmail", args)).Error);
        Assert.Empty(rig.Nav.Emails);
        if (args.Contains("accountId", StringComparison.Ordinal)) Assert.Empty(rig.Api.F.Server.ApiCalls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""["x"]""")]
    [InlineData("""[{}]""")]
    [InlineData("""[{"id":""}]""")]
    [InlineData("""[{"id":"x","extra":1}]""")]
    [InlineData("""[{"id":"x","accountId":5}]""")]
    public async Task OpenEmail_checks_its_argument(string args)
    {
        var rig = await PluginNavigationRig.StartAsync(["email:read"]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.openEmail", args)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task OpenEmail_the_app_refusing_is_one_plain_failure()
    {
        var rig = await PluginNavigationRig.StartAsync(["email:read"]);
        rig.Nav.EmailResult = PluginOpenEmailOutcome.Refused;
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("ui.openEmail", $$"""[{"id":{{Msg(rig.IdOf("Flagged reminder"))}}}]""")).Error);
    }

    [Fact]
    public async Task OpenEmail_is_limited_to_30_a_minute_per_add_on()
    {
        var rig = await PluginNavigationRig.StartAsync(["email:read"]);
        var args = $$"""[{"id":{{Msg(rig.IdOf("Flagged reminder"))}}}]""";
        for (var i = 0; i < 30; i++) Assert.True((await rig.CallAsync("ui.openEmail", args)).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("ui.openEmail", args)).Error);
        Assert.Equal(30, rig.Nav.Emails.Count);
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await rig.CallAsync("ui.openEmail", args)).IsSuccess);
    }

    [Fact]
    public async Task OpenEmail_does_not_move_the_view_when_the_login_or_grant_goes_stale_before_it_does()
    {
        var args = """[{"id":"x"}]""";
        var gone = await PluginNavigationRig.StartAsync(["email:read"]);
        gone.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.NoSession, (await gone.CallAsync("ui.openEmail", args)).Error);
        Assert.Empty(gone.Nav.Emails);

        var during = await PluginNavigationRig.StartAsync(["email:read"]);
        during.Api.OnMailCall = () => during.Api.Auth.Current = PluginApiRig.Grant(["email:read"], enabled: false);
        Assert.Equal(PluginErrorCode.StaleContext, (await during.CallAsync("ui.openEmail", $$"""[{"id":{{Msg(during.IdOf("Flagged reminder"))}}}]""")).Error);
        Assert.Empty(during.Nav.Emails);
    }

    [Fact]
    public async Task OpenEmail_whose_move_outlives_the_grant_gets_no_answer()
    {
        var rig = await PluginNavigationRig.StartAsync(["email:read"]);
        rig.Nav.OnMove = () => rig.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("ui.openEmail", $$"""[{"id":{{Msg(rig.IdOf("Flagged reminder"))}}}]""")).Error);
    }

    [Theory]
    [InlineData("month", PluginCalendarView.Month, "calendar:read")]
    [InlineData("week", PluginCalendarView.Week, "ui:calendar-action")]
    [InlineData("day", PluginCalendarView.Day, "calendar:read")]
    [InlineData("agenda", PluginCalendarView.Agenda, "ui:calendar-action")]
    [InlineData("tasks", PluginCalendarView.Tasks, "ui:tasks")]
    [InlineData("tasks", PluginCalendarView.Tasks, "tasks:read")]
    public async Task OpenCalendar_moves_to_the_view_its_own_permission_allows(string view, PluginCalendarView expected, string held)
    {
        var rig = await PluginNavigationRig.StartAsync([held]);
        var result = await rig.CallAsync("ui.openCalendar", $$"""[{"view":"{{view}}"}]""");
        Assert.True(result.Value!.BooleanValue);
        Assert.Equal((rig.Api.Account.Key, expected), Assert.Single(rig.Nav.Calendars));
    }

    [Fact]
    public async Task OpenCalendar_answers_false_when_the_app_cannot_take_the_move()
    {
        var rig = await PluginNavigationRig.StartAsync(["calendar:read"]);
        rig.Nav.Answer = false;
        Assert.False((await rig.CallAsync("ui.openCalendar", """[{"view":"month"}]""")).Value!.BooleanValue);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{}]""")]
    [InlineData("""[{"view":"settings"}]""")]
    [InlineData("""[{"view":"month","date":"2026-01-01"}]""")]
    [InlineData("""["month"]""")]
    public async Task OpenCalendar_only_opens_the_calendars_own_views(string args)
    {
        var rig = await PluginNavigationRig.StartAsync(["calendar:read", "ui:tasks"]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.openCalendar", args)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task OpenCalendar_is_limited_to_10_a_minute_per_add_on_and_one_add_on_does_not_use_up_anothers()
    {
        var rig = await PluginNavigationRig.StartAsync(["calendar:read"]);
        var other = rig.Api.BindOther("other-addon", ["calendar:read"]);
        for (var i = 0; i < 10; i++) Assert.True((await rig.CallAsync("ui.openCalendar", """[{"view":"week"}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("ui.openCalendar", """[{"view":"week"}]""")).Error);
        Assert.True((await rig.Api.CallAsync(other, "ui.openCalendar", """[{"view":"week"}]""")).IsSuccess);
        Assert.Equal(11, rig.Nav.Calendars.Count);
    }

    [Theory]
    [InlineData("ui:tasks")]
    [InlineData("tasks:read")]
    public async Task OpenTask_opens_the_task_and_answers_whether_the_calendar_had_it(string held)
    {
        var rig = await PluginNavigationRig.StartAsync([held]);
        Assert.True((await rig.CallAsync("ui.openTask", """["task-1"]""")).Value!.BooleanValue);
        rig.Nav.Answer = false;
        Assert.False((await rig.CallAsync("ui.openTask", """["task-2"]""")).Value!.BooleanValue);
        Assert.Equal(new[] { (rig.Api.Account.Key, "task-1"), (rig.Api.Account.Key, "task-2") }, rig.Nav.Tasks);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[5]")]
    [InlineData("""[""]""")]
    [InlineData("""[{"id":"t"}]""")]
    public async Task OpenTask_checks_its_argument(string args)
    {
        var rig = await PluginNavigationRig.StartAsync(["ui:tasks"]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.openTask", args)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task OpenTask_is_limited_to_20_a_minute_and_goes_stale_with_the_grant()
    {
        var rig = await PluginNavigationRig.StartAsync(["ui:tasks"]);
        for (var i = 0; i < 20; i++) Assert.True((await rig.CallAsync("ui.openTask", """["t"]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("ui.openTask", """["t"]""")).Error);
        Assert.Equal(20, rig.Nav.Tasks.Count);

        var revoked = await PluginNavigationRig.StartAsync(["ui:tasks"]);
        revoked.Api.Auth.Current = PluginApiRig.Grant(["ui:tasks"], enabled: false);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await revoked.CallAsync("ui.openTask", """["t"]""")).Error);
        Assert.Empty(revoked.Nav.Tasks);
    }

    [Theory]
    [InlineData("ui.refreshRowBadges", "badges")]
    [InlineData("ui.rerenderEmail", "email")]
    [InlineData("ui.rerenderFetchedEmails", "fetched")]
    public async Task Refresh_methods_need_no_permission_and_ask_the_views_once(string method, string expected)
    {
        var rig = await PluginNavigationRig.StartAsync([]);
        var result = await rig.CallAsync(method, "[]");
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Undefined, result.Value!.Kind);
        Assert.Equal([expected], rig.Views.Calls);
    }

    [Fact]
    public async Task RefreshRowBadges_drops_calls_over_its_budget_without_an_error()
    {
        var rig = await PluginNavigationRig.StartAsync([]);
        for (var i = 0; i < 20; i++) Assert.True((await rig.CallAsync("ui.refreshRowBadges", "[]")).IsSuccess);
        Assert.Equal(12, rig.Views.Calls.Count);
    }

    [Theory]
    [InlineData("ui.rerenderEmail")]
    [InlineData("ui.rerenderFetchedEmails")]
    public async Task Rerender_methods_are_limited_to_6_a_minute_each(string method)
    {
        var rig = await PluginNavigationRig.StartAsync([]);
        for (var i = 0; i < 6; i++) Assert.True((await rig.CallAsync(method, "[]")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync(method, "[]")).Error);
        Assert.Equal(6, rig.Views.Calls.Count);
    }

    [Fact]
    public async Task Rerender_after_the_grant_is_revoked_asks_nothing()
    {
        var rig = await PluginNavigationRig.StartAsync([]);
        rig.Api.Auth.Current = PluginApiRig.Grant([], enabled: false);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("ui.rerenderEmail", "[]")).Error);
        Assert.Empty(rig.Views.Calls);
    }

    [Fact]
    public async Task DownloadFile_asks_the_user_where_and_saves_the_add_ons_bytes_under_a_safe_name()
    {
        var rig = await PluginNavigationRig.StartAsync(["ui:download-file"]);
        var result = await rig.CallAsync("ui.downloadFile", """[{"content":"héllo","filename":"../../evil.txt","contentType":"text/plain"}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Undefined, result.Value!.Kind);
        var (name, content) = Assert.Single(rig.Files.Saved);
        Assert.Equal("_.._evil.txt", name);
        Assert.Equal("héllo"u8.ToArray(), content);

        var builder = new PluginValueBuilder();
        var bytes = builder.Array(builder.Object(("content", builder.Buffer([1, 2, 3])), ("filename", builder.String("data.bin"))));
        Assert.True((await rig.Api.Dispatcher.DispatchAsync(rig.Api.Binding, new PluginApiRequest("r", "ui.downloadFile", builder.Build(bytes)), Ct)).IsSuccess);
        Assert.Equal(new byte[] { 1, 2, 3 }, rig.Files.Saved[1].Content);
    }

    [Theory]
    [InlineData("""[{"content":"x","filename":"run.exe"}]""")]
    [InlineData("""[{"content":"x","filename":"page.HTML "}]""")]
    [InlineData("""[{"content":"x","filename":"a.svg"}]""")]
    [InlineData("""[{"content":"x","filename":"a.txt","contentType":"text/html; charset=utf-8"}]""")]
    [InlineData("""[{"content":"x","filename":"a.txt","contentType":" application/javascript"}]""")]
    [InlineData("""[{"content":5,"filename":"a.txt"}]""")]
    [InlineData("""[{"content":"x"}]""")]
    [InlineData("""[{"content":"x","filename":7}]""")]
    [InlineData("""[{"content":"x","filename":"a.txt","contentType":3}]""")]
    [InlineData("""[{"content":"x","filename":"a.txt","path":"c:\\x"}]""")]
    [InlineData("[]")]
    public async Task DownloadFile_refuses_programs_scripts_active_documents_and_bad_arguments_before_any_dialog(string args)
    {
        var rig = await PluginNavigationRig.StartAsync(["ui:download-file"]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.downloadFile", args)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task DownloadFile_closing_the_dialog_writes_nothing_and_a_failed_write_is_one_plain_failure()
    {
        var rig = await PluginNavigationRig.StartAsync(["ui:download-file"]);
        rig.Files.Result = PluginSaveOutcome.Cancelled;
        Assert.True((await rig.CallAsync("ui.downloadFile", """[{"content":"x","filename":"a.txt"}]""")).IsSuccess);
        Assert.Empty(rig.Files.Saved);
        rig.Files.Result = PluginSaveOutcome.Failed;
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("ui.downloadFile", """[{"content":"x","filename":"a.txt"}]""")).Error);
    }

    [Fact]
    public async Task DownloadFile_writes_nothing_when_the_grant_is_revoked_while_the_dialog_is_open()
    {
        var rig = await PluginNavigationRig.StartAsync(["ui:download-file"]);
        rig.Files.WhileOpen = () => rig.Api.Auth.Current = PluginApiRig.Grant(["ui:download-file"], enabled: false);
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("ui.downloadFile", """[{"content":"x","filename":"a.txt"}]""")).Error);
        Assert.Empty(rig.Files.Saved);
    }

    [Fact]
    public async Task DownloadFile_allows_one_dialog_at_a_time_and_5_a_minute()
    {
        var rig = await PluginNavigationRig.StartAsync(["ui:download-file"]);
        var release = new TaskCompletionSource();
        rig.Files.Hold = release.Task;
        var first = rig.CallAsync("ui.downloadFile", """[{"content":"x","filename":"a.txt"}]""");
        while (rig.Files.Asked.Count == 0) await Task.Delay(10, Ct);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("ui.downloadFile", """[{"content":"y","filename":"b.txt"}]""")).Error);
        release.SetResult();
        Assert.True((await first).IsSuccess);
        Assert.Equal(["a.txt"], rig.Files.Asked);

        rig.Files.Hold = null;
        for (var i = 0; i < 3; i++) Assert.True((await rig.CallAsync("ui.downloadFile", """[{"content":"x","filename":"a.txt"}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("ui.downloadFile", """[{"content":"x","filename":"a.txt"}]""")).Error);
    }
}
