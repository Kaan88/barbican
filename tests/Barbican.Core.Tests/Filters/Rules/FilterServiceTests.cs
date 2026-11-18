using Barbican.Core.Settings.Folders;
using Barbican.Core.Settings.Vacation;
using Barbican.Core.Sieve;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Filters.Rules;

public sealed class FilterServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Filter_save_round_trips_rules_and_unknown_properties()
    {
        var rig = await FilterTestRig.StartAsync();
        var rule = SieveScript.Rule("News", "from", "contains", "news@example.org", "move", "News", false);
        rule["custom"] = "retained";
        var saved = await rig.Filters.SaveAsync(FilterTestRig.Account, [rule], Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        var loaded = await rig.Filters.LoadAsync(FilterTestRig.Account, Ct);
        Assert.True(loaded.IsOk, loaded.Error?.Message);
        Assert.Equal("retained", (string?)Assert.Single(loaded.Value!.Rules)["custom"]);
    }

    [Fact]
    public async Task Vacation_plain_fields_round_trip_and_invalid_dates_are_refused()
    {
        var rig = await FilterTestRig.StartAsync();
        var value = new VacationSettings(true, "Away", "Back soon", null, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), null, null, false);
        var saved = await rig.Vacation.SaveAsync(FilterTestRig.Account, value, Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        var loaded = await rig.Vacation.LoadAsync(FilterTestRig.Account, Ct);
        Assert.True(loaded.IsOk, loaded.Error?.Message);
        Assert.Equal(value, loaded.Value);
        Assert.False((await rig.Vacation.SaveAsync(FilterTestRig.Account, value with { To = value.From!.Value.AddDays(-1) }, Ct)).IsOk);
    }

    [Fact]
    public async Task Vacation_server_failure_does_not_report_success()
    {
        var rig = await FilterTestRig.StartAsync();
        rig.Api.FailNextVacationSet("serverFail");
        var value = new VacationSettings(true, "Away", "Back soon", null, null, null, null, null, false);
        var result = await rig.Vacation.SaveAsync(FilterTestRig.Account, value, Ct);
        Assert.False(result.IsOk);
        var loaded = await rig.Vacation.LoadAsync(FilterTestRig.Account, Ct);
        Assert.True(loaded.IsOk, loaded.Error?.Message);
        Assert.False(loaded.Value!.Enabled);
    }

    [Fact]
    public async Task Vacation_calendar_block_tracks_changes_and_preserves_manual_edits()
    {
        var calendars = new FakeCalendars();
        calendars.AddCalendar("Work", isDefault: true);
        var rig = await FilterTestRig.StartAsync(new FakeJmapOptions { SieveCapability = true, VacationCapability = true, MetadataCapability = true, CalendarsCapability = true, Calendars = calendars });
        var value = new VacationSettings(true, "Away", "Back soon", null, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), null, null, true);
        Assert.True((await rig.Vacation.SaveAsync(FilterTestRig.Account, value, Ct)).IsOk);
        var created = Assert.Single(calendars.Events());
        Assert.Equal("bulwark-ooo-b", (string?)created["uid"]);
        Assert.True((bool?)created["keywords"]?["bulwark-out-of-office"]);
        Assert.True((await rig.Vacation.SaveAsync(FilterTestRig.Account, value with { To = new DateOnly(2026, 12, 4) }, Ct)).IsOk);
        Assert.Equal("P4D", (string?)Assert.Single(calendars.Events())["duration"]);
        var eventId = (string)created["id"]!;
        calendars.Update("CalendarEvent", eventId, e => { e["title"] = "Personal edit"; e["start"] = "2026-12-02T00:00:00"; });
        Assert.True((await rig.Vacation.SaveAsync(FilterTestRig.Account, value with { Enabled = false }, Ct)).IsOk);
        Assert.Equal("Personal edit", (string?)Assert.Single(calendars.Events())["title"]);
    }

    [Fact]
    public async Task Vacation_calendar_turning_off_removes_a_future_unchanged_block()
    {
        var calendars = new FakeCalendars();
        calendars.AddCalendar("Work", isDefault: true);
        var rig = await FilterTestRig.StartAsync(new FakeJmapOptions { SieveCapability = true, VacationCapability = true, MetadataCapability = true, CalendarsCapability = true, Calendars = calendars });
        var value = new VacationSettings(true, "Away", "Back soon", null, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), null, null, true);
        Assert.True((await rig.Vacation.SaveAsync(FilterTestRig.Account, value, Ct)).IsOk);
        Assert.Single(calendars.Events());
        Assert.True((await rig.Vacation.SaveAsync(FilterTestRig.Account, value with { Enabled = false }, Ct)).IsOk);
        Assert.Empty(calendars.Events());
    }

    [Fact]
    public async Task Vacation_calendar_does_not_restore_a_deleted_block_on_load()
    {
        var calendars = new FakeCalendars();
        calendars.AddCalendar("Work", isDefault: true);
        var rig = await FilterTestRig.StartAsync(new FakeJmapOptions { SieveCapability = true, VacationCapability = true, MetadataCapability = true, CalendarsCapability = true, Calendars = calendars });
        var value = new VacationSettings(true, "Away", "Back soon", null, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), null, null, true);
        Assert.True((await rig.Vacation.SaveAsync(FilterTestRig.Account, value, Ct)).IsOk);
        Assert.True(calendars.Destroy("CalendarEvent", (string)Assert.Single(calendars.Events())["id"]!));
        Assert.True((await rig.Vacation.LoadAsync(FilterTestRig.Account, Ct)).IsOk);
        Assert.Empty(calendars.Events());
    }

    [Fact]
    public async Task Vacation_calendar_turning_off_shortens_a_started_block()
    {
        var calendars = new FakeCalendars();
        calendars.AddCalendar("Work", isDefault: true);
        var rig = await FilterTestRig.StartAsync(new FakeJmapOptions { SieveCapability = true, VacationCapability = true, MetadataCapability = true, CalendarsCapability = true, Calendars = calendars });
        var value = new VacationSettings(true, "Away", "Back soon", null, new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 20), null, null, true);
        Assert.True((await rig.Vacation.SaveAsync(FilterTestRig.Account, value, Ct)).IsOk);
        Assert.True((await rig.Vacation.SaveAsync(FilterTestRig.Account, value with { Enabled = false }, Ct)).IsOk);
        Assert.Equal("PT9H", (string?)Assert.Single(calendars.Events())["duration"]);
    }

    [Fact]
    public async Task Folder_and_template_changes_round_trip()
    {
        var rig = await FilterTestRig.StartAsync();
        var folder = await rig.Folders.CreateAsync(FilterTestRig.Account, null, "Project", Ct);
        Assert.True(folder.IsOk, folder.Error?.Message);
        Assert.True((await rig.Folders.SetPrefAsync(FilterTestRig.Account, folder.Value!, FolderAdminPref.Pin, "true", Ct)).IsOk);
        var prefs = await rig.Folders.PrefsAsync(FilterTestRig.Account, Ct);
        Assert.True(prefs.IsOk, prefs.Error?.Message);
        Assert.Contains(folder.Value!, prefs.Value!.Pinned);
        var template = await rig.Templates.CreateAsync(FilterTestRig.Account, "Greeting", "Hello", "Body", false, Ct);
        Assert.True(template.IsOk, template.Error?.Message);
        var updated = await rig.Templates.UpdateAsync(FilterTestRig.Account, template.Value! with { Subject = "Updated" }, Ct);
        Assert.True(updated.IsOk, updated.Error?.Message);
        var list = await rig.Templates.ListAsync(FilterTestRig.Account, Ct);
        Assert.Equal("Updated", Assert.Single(list.Value!).Subject);
        Assert.True((await rig.Templates.DeleteAsync(FilterTestRig.Account, template.Value!.Id, Ct)).IsOk);
        Assert.Empty((await rig.Templates.ListAsync(FilterTestRig.Account, Ct)).Value!);
    }
}
