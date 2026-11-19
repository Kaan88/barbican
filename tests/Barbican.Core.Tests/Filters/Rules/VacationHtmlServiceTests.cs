using Barbican.Core.Settings.Vacation;

namespace Barbican.Core.Tests.Filters.Rules;

public sealed class VacationHtmlServiceTests
{
    [Fact]
    public async Task Html_and_plain_bodies_round_trip_through_the_real_service_and_fake_server()
    {
        var ct = TestContext.Current.CancellationToken;
        var rig = await FilterTestRig.StartAsync();
        var initial = await rig.Vacation.LoadSnapshotAsync(FilterTestRig.Account, ct);
        Assert.True(initial.IsOk, initial.Error?.Message);
        var value = initial.Value! with { Enabled = true, Subject = "Away", TextBody = "Plain reply", HtmlBody = "<p>Rich <b>reply</b></p>", BlockCalendar = false };
        var saved = await rig.Vacation.SaveAsync(FilterTestRig.Account, value, ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        var loaded = await rig.Vacation.LoadAsync(FilterTestRig.Account, ct);
        Assert.True(loaded.IsOk, loaded.Error?.Message);
        Assert.Equal(value.TextBody, loaded.Value!.TextBody);
        Assert.Equal(value.HtmlBody, loaded.Value.HtmlBody);
        var cleared = await rig.Vacation.SaveAsync(FilterTestRig.Account, loaded.Value! with { HtmlBody = null }, ct);
        Assert.True(cleared.IsOk, cleared.Error?.Message);
        loaded = await rig.Vacation.LoadAsync(FilterTestRig.Account, ct);
        Assert.Null(loaded.Value!.HtmlBody);
        Assert.Equal(value.TextBody, loaded.Value.TextBody);
    }
}
