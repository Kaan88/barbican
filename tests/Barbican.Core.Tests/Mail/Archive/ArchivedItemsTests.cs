// x:ArchivedItem list and restore against the fake (Bulwark lib/jmap/client.ts:13411-13500; Stalwart archived_item.rs). J-074.

using Barbican.Core.Mail.Archive;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Archive;

public sealed class ArchivedItemsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Fake> StartAsync(bool refuseFilter = false)
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { ArchiveCapability = true, ArchiveRefusesAccountFilter = refuseFilter });
        f.Server.Api.AddArchived("a1", new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero), "Old invoice");
        f.Server.Api.AddArchived("a2", new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero), "Newer note");
        return f;
    }

    [Fact]
    [Rfc("J-074", "Stalwart x:ArchivedItem")]
    public async Task The_archive_lists_newest_first_and_a_restore_removes_the_item()
    {
        var f = await StartAsync();
        Assert.True(ArchivedItems.Advertised(f.Client));
        var list = await ArchivedItems.ListAsync(f.Client, 500, Ct);
        Assert.True(list.IsOk, list.Error?.Message);
        Assert.Equal("a2,a1", string.Join(",", list.Value!.Select(x => x.Id)));
        Assert.Equal("Newer note", list.Value![0].Subject);

        var restored = await ArchivedItems.RestoreAsync(f.Client, "a2", Ct);
        Assert.True(restored.IsOk, restored.Error?.Message);
        Assert.Equal("a1", string.Join(",", f.Server.Api.ArchivedIds));
        Assert.False((await ArchivedItems.RestoreAsync(f.Client, "a2", Ct)).IsOk);
    }

    [Fact]
    [Rfc("J-074", "Stalwart x:ArchivedItem")]
    public async Task A_server_that_refuses_the_account_filter_is_asked_again_without_it()
    {
        var f = await StartAsync(refuseFilter: true);
        var list = await ArchivedItems.ListAsync(f.Client, 500, Ct);
        Assert.True(list.IsOk, list.Error?.Message);
        Assert.Equal(2, list.Value!.Count);
    }

    [Fact]
    [Rfc("J-074", "Stalwart x:ArchivedItem")]
    public async Task A_server_without_the_capability_hides_the_archive()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { Stalwart = false });
        Assert.False(ArchivedItems.Advertised(f.Client));
        Assert.False((await ArchivedItems.ListAsync(f.Client, 500, Ct)).IsOk);
    }
}
