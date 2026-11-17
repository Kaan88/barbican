#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (as the ported Rust asserts)
using System.Text.Json.Nodes;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Tests.Compose;

/// <summary>Drafts: one Email/set creates the new draft and destroys the previous (B-COMP-001..006, J-015 creation ids), the message the server stores, and the autosave hooks. FakeJmap-based, no network.</summary>
public sealed class DraftServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_saves_in_a_row_leave_one_draft()
    {
        var rig = await PipelineRig.StartAsync();
        var first = await rig.Drafts.SaveAsync(PipelineRig.NewDraft(), Ct);
        Assert.True(first.IsOk, first.Error?.Message);
        var second = await rig.Drafts.SaveAsync(PipelineRig.NewDraft(subject: "Hello again") with { DraftId = first.Value!.DraftId }, Ct);
        Assert.True(second.IsOk, second.Error?.Message);
        var drafts = rig.InFolder("drafts");
        var only = Assert.Single(drafts);
        Assert.Equal(second.Value!.DraftId, only);
        var email = rig.Store.Email(only)!;
        Assert.Equal("Hello again", (string?)email["subject"]);
        Assert.True(email["keywords"]!["$draft"]!.GetValue<bool>());
        Assert.Contains("erin@example.net", email["to"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_draft_has_the_text_and_html_parts_and_the_identity_as_from()
    {
        var rig = await PipelineRig.StartAsync();
        var saved = await rig.Drafts.SaveAsync(PipelineRig.NewDraft(body: "Hello <b>world"), Ct);
        var email = rig.Store.Email(saved.Value!.DraftId)!;
        Assert.Equal("user@example.org", (string?)email["from"]![0]!["email"]);
        var values = email["bodyValues"]!.AsObject().Select(kv => (string?)kv.Value!["value"]).ToList();
        Assert.Contains("Hello <b>world", values);
        Assert.Contains(values, v => v!.Contains("&lt;b&gt;world", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discard_removes_the_draft_and_a_missing_one_is_fine()
    {
        var rig = await PipelineRig.StartAsync();
        var saved = await rig.Drafts.SaveAsync(PipelineRig.NewDraft(), Ct);
        Assert.True((await rig.Drafts.DiscardAsync(PipelineRig.Account, saved.Value!.DraftId, Ct)).IsOk);
        Assert.Empty(rig.InFolder("drafts"));
        Assert.True((await rig.Drafts.DiscardAsync(PipelineRig.Account, saved.Value.DraftId, Ct)).IsOk);
    }

    [Fact]
    public async Task Attachments_inline_images_receipt_and_priority_go_into_the_message()
    {
        var rig = await PipelineRig.StartAsync();
        var png = rig.Store.PutBlob([1, 2, 3]);
        var pdf = rig.Store.PutBlob([4, 5]);
        var draft = PipelineRig.NewDraft() with
        {
            Body = new Doc([Block.Para("see"), new Block(new BlockKind.Image(new ImageData { Src = "cid:pic@x", Cid = "pic@x", BlobId = png }))]),
            Attachments =
            [
                new DraftAttachment { Name = "pic.png", Type = "image/png", Size = 3, BlobId = png, Cid = "pic@x" },
                new DraftAttachment { Name = "doc.pdf", Type = "application/pdf", Size = 2, BlobId = pdf },
                new DraftAttachment { Name = "unused.png", Type = "image/png", Size = 3, BlobId = png, Cid = "unused@x" },
            ],
            ReadReceipt = true,
            Priority = MessagePriority.High,
        };
        var saved = await rig.Drafts.SaveAsync(draft, Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        var email = rig.Store.Email(saved.Value!.DraftId)!;
        var names = email["attachments"]!.AsArray().Select(a => (string?)a!["name"]).ToList();
        Assert.Equal(["pic.png", "doc.pdf"], names);
        Assert.Equal("pic@x", (string?)email["attachments"]![0]!["cid"]);
        Assert.Equal("inline", (string?)email["attachments"]![0]!["disposition"]);
        Assert.Contains(email["headers"]!.AsArray(), h => (string?)h!["name"] == "Disposition-Notification-To" && ((string?)h["value"])!.Contains("user@example.org", StringComparison.Ordinal));
        Assert.Contains(email["headers"]!.AsArray(), h => (string?)h!["name"] == "X-Priority" && ((string?)h["value"])!.Contains('1', StringComparison.Ordinal));
        Assert.Contains(email["headers"]!.AsArray(), h => (string?)h!["name"] == "Importance");
    }

    [Fact]
    public async Task A_blob_that_was_never_uploaded_is_refused_with_words()
    {
        var rig = await PipelineRig.StartAsync();
        var r = await rig.Drafts.SaveAsync(PipelineRig.NewDraft() with { Attachments = [new DraftAttachment { Name = "x.bin", BlobId = "nope" }] }, Ct);
        Assert.False(r.IsOk);
        Assert.Contains("attach it again", r.Error!.Message, StringComparison.Ordinal);
        Assert.Empty(rig.InFolder("drafts"));
    }

    [Fact]
    public async Task A_from_override_goes_into_the_header_and_an_unknown_identity_is_an_error_not_a_draft_from_someone_else()
    {
        var rig = await PipelineRig.StartAsync();

        // the composer's own identity sends it (Rust pick_identity_for: the override's identity, the composer's, a wildcard, else nothing for an unmatched address with no composer identity)
        var ok = await rig.Drafts.SaveAsync(PipelineRig.NewDraft() with { FromOverride = new Barbican.Core.Jmap.Submission.FromOverride("X", "who@elsewhere.example") }, Ct);
        Assert.True(ok.IsOk, ok.Error?.Message);
        Assert.Equal("who@elsewhere.example", (string?)rig.Store.Email(ok.Value!.DraftId)!["from"]![0]!["email"]);
        var r = await rig.Drafts.SaveAsync(PipelineRig.NewDraft() with { IdentityId = "gone", FromOverride = new Barbican.Core.Jmap.Submission.FromOverride("X", "who@elsewhere.example") }, Ct);
        Assert.False(r.IsOk);
        Assert.Contains("may send as who@elsewhere.example", r.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plugin_can_skip_an_autosave_but_never_an_explicit_save()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Hooks.Transform<AlmostSavedDraftPayload>(HookNames.OnBeforeDraftAutoSave, (p, _) => ValueTask.FromResult(p with { Skip = true, Subject = "changed" }));
        var skipped = await rig.Drafts.SaveAsync(PipelineRig.NewDraft(), true, Ct);
        Assert.True(skipped.IsOk);
        Assert.Empty(rig.InFolder("drafts"));
        var explicitSave = await rig.Drafts.SaveAsync(PipelineRig.NewDraft(), Ct);
        Assert.True(explicitSave.IsOk);
        Assert.Equal("changed", (string?)rig.Store.Email(Assert.Single(rig.InFolder("drafts")))!["subject"]);
    }

    [Fact]
    public async Task The_saved_hook_is_told_once_the_draft_exists()
    {
        var rig = await PipelineRig.StartAsync();
        var told = new TaskCompletionSource<DraftSavedPayload>();
        rig.Hooks.Observe<DraftSavedPayload>(HookNames.OnDraftAutoSave, (p, _) =>
        {
            told.TrySetResult(p);
            return ValueTask.CompletedTask;
        });
        var saved = await rig.Drafts.SaveAsync(PipelineRig.NewDraft(), Ct);
        var payload = await told.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, Ct);
        Assert.Equal(saved.Value!.DraftId, payload.DraftId);
        Assert.Equal("Hello", payload.Subject);
    }

    [Fact]
    public void Builder_leaves_cc_and_bcc_out_when_empty_and_keeps_reply_headers()
    {
        var draft = new ComposeDraft { AccountId = "a", IdentityId = "i1", To = "a@x.example", Subject = "s", Body = new Doc([Block.Para("b")]), InReplyTo = "<m1@x>", References = ["<m0@x>", "m1@x"] };
        var identity = new Barbican.Core.Mail.Model.Identity("i1", "Me", "me@x.example");
        var o = DraftBuilder.Build(draft, identity, "me@x.example", "d1", true, "<p>b</p>", "b");
        Assert.Null(o["cc"]);
        Assert.Null(o["bcc"]);
        Assert.Equal("m1@x", (string?)o["inReplyTo"]![0]);
        Assert.Equal(["m0@x", "m1@x"], o["references"]!.AsArray().Select(r => (string?)r));
        Assert.Equal("multipart/alternative", (string?)o["bodyStructure"]!["type"]);
        _ = (JsonNode)o;
    }
}
