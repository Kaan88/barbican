// Synthetic fake-server coverage for vacation snapshot, state protection, and read-only behavior.
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Sieve;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Filters.Rules;

public sealed class VacationConcurrencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_loaded_editors_cannot_overwrite_one_another()
    {
        var rig = await FilterTestRig.StartAsync();
        var first = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        var stale = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        var saved = await rig.Vacation.SaveAsync(FilterTestRig.Account, first with { Subject = "First" }, Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        var refused = await rig.Vacation.SaveAsync(FilterTestRig.Account, stale with { Subject = "Stale" }, Ct);
        Assert.False(refused.IsOk);
        Assert.Equal("stateMismatch", Assert.IsType<MethodError>(refused.Error).Type);
        Assert.Equal("First", rig.Api.CurrentVacation.Subject);
        Assert.Single(rig.Api.VacationSetRequests);
    }

    [Fact]
    public async Task A_per_object_refusal_keeps_its_typed_error_and_does_not_notify_success()
    {
        var rig = await FilterTestRig.StartAsync();
        var loaded = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        var changed = 0;
        rig.Vacation.Changed += (_, _) => changed++;
        rig.Api.FailNextVacationSet("forbidden");
        var result = await rig.Vacation.SaveAsync(FilterTestRig.Account, loaded with { Subject = "Refused" }, Ct);
        Assert.False(result.IsOk);
        Assert.Equal("forbidden", Assert.IsType<SetError>(result.Error).Type);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task State_change_during_hook_is_rejected_by_the_server_guard()
    {
        var rig = await FilterTestRig.StartAsync();
        var loaded = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        using var hook = rig.Hooks.Intercept<VacationHookPayload>(HookNames.OnBeforeVacationUpdate, async (_, ct) =>
        {
            var args = new JsonObject { ["accountId"] = rig.Fake.Client.Session.MailAccountId, ["update"] = new JsonObject { ["singleton"] = new JsonObject { ["subject"] = "Concurrent" } } };
            var remote = await rig.Fake.Client.CallAsync(JmapRequest.Of(JmapRequest.Call("VacationResponse/set", args, "test")), ct);
            Assert.True(remote.IsOk);
            return true;
        });
        var refused = await rig.Vacation.SaveAsync(FilterTestRig.Account, loaded with { Subject = "Stale" }, Ct);
        Assert.False(refused.IsOk);
        Assert.Equal("stateMismatch", Assert.IsType<MethodError>(refused.Error).Type);
        Assert.Equal("Concurrent", rig.Api.CurrentVacation.Subject);
        Assert.Equal(loaded.State, (string?)rig.Api.VacationSetRequests[^1]["ifInState"]);
    }

    [Fact]
    public async Task Changed_sieve_state_is_refused_before_vacation_response_write()
    {
        var rig = await FilterTestRig.StartAsync();
        var loaded = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        rig.Api.SeedSieve("external", "# external", true);
        var refused = await rig.Vacation.SaveAsync(FilterTestRig.Account, loaded with { Subject = "Stale" }, Ct);
        Assert.False(refused.IsOk);
        Assert.Equal("stateMismatch", Assert.IsType<MethodError>(refused.Error).Type);
        Assert.Empty(rig.Api.VacationSetRequests);
    }

    [Fact]
    public async Task Toggle_preserves_unchanged_raw_dates_and_null_fields()
    {
        var original = new FakeVacation { IsEnabled = false, FromDate = "2026-12-01T09:31:22Z", ToDate = "2026-12-04T14:37:01Z", Subject = null, TextBody = "Away", HtmlBody = null };
        var rig = await FilterTestRig.StartAsync(new FakeJmapOptions { VacationCapability = true, Vacation = original });
        var loaded = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        var saved = await rig.Vacation.SaveAsync(FilterTestRig.Account, loaded with { Enabled = true }, Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        var patch = Assert.IsType<JsonObject>(rig.Api.VacationSetRequests[^1]["update"]!["singleton"]);
        Assert.Single(patch);
        Assert.True((bool?)patch["isEnabled"]);
        Assert.Equal(original with { IsEnabled = true }, rig.Api.CurrentVacation);
    }

    [Fact]
    public async Task Shared_stalwart_state_preserves_managed_rules_and_never_overwrites_reserved_vacation()
    {
        var options = new FakeJmapOptions { SieveCapability = true, VacationCapability = true, VacationSharesSieveState = true, SieveExtensions = ["include"] };
        var rig = await FilterTestRig.StartAsync(options);
        var rule = SieveScript.Rule("Keep this", "from", "contains", "a@example.org", "mark_read", "", false);
        rig.Api.SeedSieve("filters", SieveScript.Generate([rule], string.Empty), true);
        var initial = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        Assert.Equal(initial.State, initial.SieveState);
        var on = await rig.Vacation.SaveAsync(FilterTestRig.Account, initial with { Enabled = true, TextBody = "Away" }, Ct);
        Assert.True(on.IsOk, on.Error?.Message);
        Assert.Equal("# fake reserved vacation", rig.Api.SieveText("vacation"));
        Assert.Equal("Keep this", (string?)Assert.Single((SieveScript.Parse(rig.Api.SieveText("filters")!).Metadata["rules"] as JsonArray)!)!["name"]);
        Assert.Contains("include", rig.Api.ActiveSieveText(), StringComparison.Ordinal);
        var off = await rig.Vacation.SaveAsync(FilterTestRig.Account, on.Value! with { Enabled = false }, Ct);
        Assert.True(off.IsOk, off.Error?.Message);
        Assert.Equal("# fake reserved vacation", rig.Api.SieveText("vacation"));
        Assert.Equal("Keep this", (string?)Assert.Single((SieveScript.Parse(rig.Api.SieveText("filters")!).Metadata["rules"] as JsonArray)!)!["name"]);
    }

    [Fact]
    public async Task Concurrent_sieve_change_after_vacation_write_reports_partial_and_does_not_overwrite_filters()
    {
        var options = new FakeJmapOptions { SieveCapability = true, VacationCapability = true, VacationSharesSieveState = true, SieveExtensions = ["include"] };
        var rig = await FilterTestRig.StartAsync(options);
        var rule = SieveScript.Rule("Keep this", "from", "contains", "a@example.org", "mark_read", "", false);
        var original = SieveScript.Generate([rule], string.Empty);
        rig.Api.SeedSieve("filters", original, true);
        var initial = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        rig.Api.AfterNextVacationSet(() => rig.Api.SeedSieve("concurrent", "# concurrent", false));
        var result = await rig.Vacation.SaveAsync(FilterTestRig.Account, initial with { Enabled = true, TextBody = "Away" }, Ct);
        Assert.False(result.IsOk);
        Assert.Contains("may have been saved", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(rig.Api.CurrentVacation.IsEnabled);
        Assert.Equal(original, rig.Api.SieveText("filters"));
        Assert.Equal("# concurrent", rig.Api.SieveText("concurrent"));
    }

    [Fact]
    public async Task Shared_stalwart_vacation_without_filters_does_not_create_or_overwrite_a_filter_script()
    {
        var options = new FakeJmapOptions { SieveCapability = true, VacationCapability = true, VacationSharesSieveState = true, SieveExtensions = ["include"] };
        var rig = await FilterTestRig.StartAsync(options);
        var initial = (await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct)).Value!;
        var on = await rig.Vacation.SaveAsync(FilterTestRig.Account, initial with { Enabled = true, TextBody = "Away" }, Ct);
        Assert.True(on.IsOk, on.Error?.Message);
        Assert.Equal("# fake reserved vacation", rig.Api.SieveText("vacation"));
        Assert.Null(rig.Api.SieveText("barbican"));
        var off = await rig.Vacation.SaveAsync(FilterTestRig.Account, on.Value! with { Enabled = false }, Ct);
        Assert.True(off.IsOk, off.Error?.Message);
        Assert.Equal("# fake reserved vacation", rig.Api.SieveText("vacation"));
        Assert.Null(rig.Api.SieveText("barbican"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Snapshot_and_readonly_load_do_not_start_calendar_reconciliation(bool readOnly)
    {
        var calendars = new FakeCalendars();
        calendars.AddCalendar("Work", isDefault: true);
        var vacation = new FakeVacation { IsEnabled = true, TextBody = "Away", FromDate = "2026-12-01T00:00:00Z", ToDate = "2026-12-03T23:59:59Z" };
        var rig = await FilterTestRig.StartAsync(new FakeJmapOptions { VacationCapability = true, Vacation = vacation, CalendarsCapability = true, Calendars = calendars }, readOnly);
        var result = readOnly ? await rig.Vacation.LoadAsync(FilterTestRig.Account, Ct) : await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Empty(calendars.Events());
        Assert.DoesNotContain(rig.Fake.Server.ApiCalls, call => call.Contains("Calendar/get", StringComparison.Ordinal) || call.Contains("CalendarEvent/set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ordinary_writable_load_retains_source_calendar_reconciliation()
    {
        var calendars = new FakeCalendars();
        calendars.AddCalendar("Work", isDefault: true);
        var vacation = new FakeVacation { IsEnabled = true, TextBody = "Away", FromDate = "2026-12-01T00:00:00Z", ToDate = "2026-12-03T23:59:59Z" };
        var rig = await FilterTestRig.StartAsync(new FakeJmapOptions { VacationCapability = true, Vacation = vacation, CalendarsCapability = true, Calendars = calendars });
        Assert.True((await rig.Vacation.LoadAsync(FilterTestRig.Account, Ct)).IsOk);
        Assert.Single(calendars.Events());
    }
}
