// R3-09: a search typed inside a tag view keeps the tag and the folders the view leaves out, the header keeps naming the tag, and clearing the box brings the
// tag list back.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature
using Barbican.FakeJmap;

namespace Barbican.Presentation.Tests.Actions;

public sealed class TagSearchTests
{
    [Fact]
    public async Task Searching_inside_a_tag_view_keeps_the_tag_and_clearing_restores_the_tag_list()
    {
        using var h = await ActionHarness.StartAsync();
        var inbox = h.Folder("inbox");
        var trash = h.Folder("trash");
        h.Server.Store.Deliver(new FakeMail { Subject = "zzqx red one", Keywords = ["$label:red"] }, inbox);
        h.Server.Store.Deliver(new FakeMail { Subject = "zzqx red two", Keywords = ["$label:red"] }, inbox);
        h.Server.Store.Deliver(new FakeMail { Subject = "zzqx plain" }, inbox);
        h.Server.Store.Deliver(new FakeMail { Subject = "zzqx binned red", Keywords = ["$label:red"] }, trash);
        h.Server.Store.Deliver(new FakeMail { Subject = "other red", Keywords = ["$label:red"] }, inbox);

        await h.List.OpenTagAsync("red", "Red", [trash]);
        Assert.Equal(3, h.List.Messages.Count());

        await h.List.SearchAsync("zzqx");
        var found = h.List.Messages.Select(m => m.Summary.Subject).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["zzqx red one", "zzqx red two"], found);
        Assert.Equal("red", h.List.TagId);
        Assert.Equal("Red", h.List.Title);

        await h.List.SearchAsync(string.Empty);
        Assert.Equal(3, h.List.Messages.Count());
        Assert.Equal("red", h.List.TagId);
        Assert.Equal("Red", h.List.Title);
    }
}
