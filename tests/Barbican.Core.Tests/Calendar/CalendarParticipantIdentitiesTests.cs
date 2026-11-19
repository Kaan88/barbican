using Barbican.Core.Calendar;
using Barbican.Core.Tests.Calendar.Editing;

namespace Barbican.Core.Tests.Calendar;

public sealed class CalendarParticipantIdentitiesTests
{
    [Fact]
    public async Task Reads_and_changes_default_and_rejects_unknown_id()
    {
        var rig = await EditingRig.CreateAsync((c, _) =>
        {
            c.AddParticipantIdentity("one", "First", "mailto:first@example.org", true);
            c.AddParticipantIdentity("two", "Second", "mailto:second@example.org");
        });
        var service = new CalendarParticipantIdentities(rig.Accounts);
        var ct = TestContext.Current.CancellationToken;
        var initial = await service.ListAsync(EditingRig.Account, ct);
        Assert.True(initial.IsOk);
        Assert.Equal("one", Assert.Single(initial.Value!, i => i.IsDefault).Id);
        Assert.True((await service.SetDefaultAsync(EditingRig.Account, "two", ct)).IsOk);
        var changed = await service.ListAsync(EditingRig.Account, ct);
        Assert.Equal("two", Assert.Single(changed.Value!, i => i.IsDefault).Id);
        Assert.False((await service.SetDefaultAsync(EditingRig.Account, "missing", ct)).IsOk);
    }

    [Fact]
    public async Task No_signed_in_account_is_an_error()
    {
        var rig = await EditingRig.CreateAsync();
        var result = await new CalendarParticipantIdentities(rig.Accounts).ListAsync("missing", TestContext.Current.CancellationToken);
        Assert.False(result.IsOk);
    }
}
