#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Compose;

/// <summary>The pipeline end to end with editor-core's real DocHtml: reply quoting, the saved HTML and a reopened draft.</summary>
public sealed class RealCodecTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reply_saved_as_a_draft_reopens_with_its_quote_and_the_inline_image_kept()
    {
        var rig = await PipelineRig.StartAsync(codec: new DocHtmlCodec());
        var source = rig.Seeded(FakeSeed.InlineThreadSubject);
        var seed = (await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.Reply) { SourceEmailId = source }, Ct)).Value!;
        Assert.Contains(seed.Body.Blocks, b => b.Kind is BlockKind.Quoted);
        var saved = await rig.Drafts.SaveAsync(new ComposeDraft { AccountId = PipelineRig.Account, IdentityId = "i1", To = seed.To, Subject = seed.Subject, Body = seed.Body, Attachments = seed.Attachments, Kind = ComposeKind.Reply, SourceEmailId = source }, Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        var email = rig.Store.Email(saved.Value!.DraftId)!;
        Assert.Contains("cid:sketch@example.net", email["bodyValues"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("sketch@example.net", (string?)email["attachments"]![0]!["cid"]);
        var again = (await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.EditDraft) { SourceEmailId = saved.Value.DraftId }, Ct)).Value!;
        Assert.Contains(again.Body.Blocks, b => b.Kind is BlockKind.Quoted);
        Assert.Equal("sketch@example.net", Assert.Single(again.Attachments).Cid);
    }
}
