#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (as the ported Rust asserts)
using System.Text.Json.Nodes;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Compose;

/// <summary>Reply, reply-all, forward, forward as attachment, new and draft reopen: recipients, subject, quote, identity, signature, attachments and inline images (B-COMP-007..018, G-058). FakeJmap-based.</summary>
public sealed class ComposeSeederTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ComposeSeed> SeedAsync(PipelineRig rig, ComposeKind kind, string? subject = null, bool plain = false)
    {
        var r = await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, kind) { SourceEmailId = subject is null ? null : rig.Seeded(subject), Plain = plain }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        return r.Value!;
    }

    private static IReadOnlyList<string> Texts(ComposeSeed s) => [.. s.Body.Blocks.Select(b => b.Kind is BlockKind.Quoted ? "<quoted>" : b.Text)];

    [Fact]
    public async Task Reply_goes_to_the_reply_to_address_with_re_once_and_threading_headers()
    {
        var rig = await PipelineRig.StartAsync();
        var seed = await SeedAsync(rig, ComposeKind.Reply, FakeSeed.ReplyToSubject);
        Assert.Equal("boss@example.net", seed.To);
        Assert.Equal("", seed.Cc);
        Assert.Equal("Re: Budget thread", seed.Subject);
        Assert.Equal("budget-1@example.net", seed.InReplyTo);
        Assert.Equal(["budget-1@example.net"], seed.References);
        Assert.Equal(rig.Seeded(FakeSeed.ReplyToSubject), seed.SourceEmailId);
        Assert.Equal("i1", seed.IdentityId);

        // "Re:" is not doubled
        rig.Store.Deliver(new FakeMail { Subject = "Re: Already", FromEmail = "x@example.net" });
        var again = await SeedAsync(rig, ComposeKind.Reply, "Re: Already");
        Assert.Equal("Re: Already", again.Subject);
    }

    [Theory]
    [InlineData(FakeSeed.ReplyToSubject, "boss@example.net")]
    [InlineData(FakeSeed.SelfSentSubject, "erin@example.net")]
    public async Task Inline_reply_keeps_the_thread_and_signature_without_quoting_the_source(string subject, string recipient)
    {
        var rig = await PipelineRig.StartAsync();
        var request = new ComposeRequest(PipelineRig.Account, ComposeKind.Reply)
        {
            SourceEmailId = rig.Seeded(subject),
            DraftText = "Thanks for the update.",
            InlineReply = true,
        };
        var seed = (await rig.Seeder.SeedAsync(request, Ct)).Value!;
        var full = await SeedAsync(rig, ComposeKind.Reply, subject);
        Assert.Equal(recipient, seed.To);
        Assert.Equal(full.InReplyTo, seed.InReplyTo);
        Assert.Equal(full.References, seed.References);
        Assert.Equal(full.SignatureTexts, seed.SignatureTexts);
        Assert.Equal(full.IdentityId, seed.IdentityId);
        Assert.Null(seed.FromOverride);
        Assert.Empty(seed.Attachments);
        Assert.Equal("Thanks for the update.", Texts(seed)[0]);
        Assert.DoesNotContain("<quoted>", Texts(seed));
    }

    [Fact]
    public async Task Reply_all_drops_own_addresses_with_their_tags_and_folds_duplicates()
    {
        var rig = await PipelineRig.StartAsync();
        var seed = await SeedAsync(rig, ComposeKind.ReplyAll, FakeSeed.ReplyToSubject);
        Assert.Equal("boss@example.net, other@example.net", seed.To);
        Assert.Equal("carl@example.net", seed.Cc);
    }

    [Fact]
    public async Task Replying_to_your_own_message_continues_the_conversation()
    {
        var rig = await PipelineRig.StartAsync();
        var seed = await SeedAsync(rig, ComposeKind.Reply, FakeSeed.SelfSentSubject);
        Assert.Equal("erin@example.net", seed.To);
    }

    [Fact]
    public async Task The_reply_quotes_the_original_collapsed_with_a_wrote_line_and_the_signature_below()
    {
        var rig = await PipelineRig.StartAsync();
        var seed = await SeedAsync(rig, ComposeKind.Reply, FakeSeed.InlineThreadSubject);
        Assert.Equal(["", "On Tue 10 Nov 2026, 09:00, Dora <dora@example.net> wrote:", "<quoted>", "", "-- ", "Test User", "Example Ltd"], Texts(seed));
        var quoted = Assert.IsType<BlockKind.Quoted>(seed.Body.Blocks[2].Kind);
        Assert.False(quoted.Open);
        Assert.True(quoted.Cite);
        Assert.Contains("cid:sketch@example.net", quoted.Html, StringComparison.Ordinal);
        Assert.Equal(["Test User\nExample Ltd"], seed.SignatureTexts);
    }

    [Fact]
    public async Task A_quoted_inline_image_is_resent_inline_in_the_reply_and_the_forward()
    {
        var rig = await PipelineRig.StartAsync();
        var reply = await SeedAsync(rig, ComposeKind.Reply, FakeSeed.InlineThreadSubject);
        var inline = Assert.Single(reply.Attachments);
        Assert.Equal("sketch@example.net", inline.Cid);
        Assert.NotNull(inline.BlobId);

        // end to end: the reply goes out with the image in multipart/related, not only a reference
        var draft = new ComposeDraft { AccountId = PipelineRig.Account, IdentityId = "i1", To = reply.To, Subject = reply.Subject, Body = reply.Body, Attachments = reply.Attachments, Kind = ComposeKind.Reply, SourceEmailId = reply.SourceEmailId };
        var sent = await rig.Sender.SendAsync(draft, new SendOptions(), Ct);
        Assert.True(sent.IsOk, sent.Error?.Message);
        var email = rig.Store.Email(sent.Value!.EmailId)!;
        var cid = Assert.Single(email["attachments"]!.AsArray());
        Assert.Equal("sketch@example.net", (string?)cid!["cid"]);
        Assert.Equal("inline", (string?)cid["disposition"]);
    }

    [Fact]
    public async Task Forward_carries_the_attachments_and_the_inline_images_and_quotes_open()
    {
        var rig = await PipelineRig.StartAsync();
        var seed = await SeedAsync(rig, ComposeKind.Forward, FakeSeed.InlineThreadSubject);
        Assert.Equal("Fwd: Design sketch", seed.Subject);
        Assert.Equal("", seed.To);
        Assert.Equal(["notes.pdf", "sketch.png"], seed.Attachments.Select(a => a.Name).Order(StringComparer.Ordinal));
        Assert.Null(seed.Attachments.Single(a => a.Name == "notes.pdf").Cid);
        Assert.Equal("sketch@example.net", seed.Attachments.Single(a => a.Name == "sketch.png").Cid);
        Assert.Equal(["", QuoteHeader.ForwardedBanner, "From: Dora <dora@example.net>\nDate: Tue 10 Nov 2026, 09:00\nSubject: Design sketch", "<quoted>", "", "-- ", "Test User", "Example Ltd"], Texts(seed));
        var quoted = Assert.IsType<BlockKind.Quoted>(seed.Body.Blocks[3].Kind);
        Assert.True(quoted.Open);
        Assert.False(quoted.Cite);
    }

    [Fact]
    public async Task Forward_as_attachment_attaches_the_messages_own_blob_and_starts_empty()
    {
        var rig = await PipelineRig.StartAsync();
        var seed = await SeedAsync(rig, ComposeKind.ForwardAsAttachment, FakeSeed.InlineThreadSubject);
        Assert.Equal("Fwd: Design sketch", seed.Subject);
        var a = Assert.Single(seed.Attachments);
        Assert.Equal("Design sketch.eml", a.Name);
        Assert.Equal("message/rfc822", a.Type);
        Assert.Equal((string?)rig.Store.Email(rig.Seeded(FakeSeed.InlineThreadSubject))!["blobId"], a.BlobId);
        Assert.DoesNotContain("<quoted>", Texts(seed));
    }

    [Fact]
    public async Task The_identity_is_the_one_the_message_was_addressed_to_and_a_catch_all_becomes_a_from_override()
    {
        var rig = await PipelineRig.StartAsync();
        var plain = await SeedAsync(rig, ComposeKind.Reply, FakeSeed.AliasSubject);
        Assert.Equal("i1", plain.IdentityId);
        Assert.Null(plain.FromOverride);

        var auto = await PipelineRig.StartAsync(prefs: JsonNode.Parse("""{"autoSelectReplyIdentity": true, "replyIdentityMatch": "domain"}""")!.AsObject());
        var seed = await SeedAsync(auto, ComposeKind.Reply, FakeSeed.AliasSubject);
        Assert.Equal("i1", seed.IdentityId);
        Assert.Equal(new Barbican.Core.Jmap.Submission.FromOverride("", "sales@example.org"), seed.FromOverride);
    }

    [Fact]
    public async Task New_message_gets_the_default_identity_and_its_html_signature_with_a_separator()
    {
        var rig = await PipelineRig.StartAsync();
        var seed = await SeedAsync(rig, ComposeKind.New);
        Assert.Equal("i1", seed.IdentityId);
        Assert.Equal(["", "", "-- ", "Test User", "Example Ltd"], Texts(seed));
        Assert.Equal(["Test User\nExample Ltd"], seed.SignatureTexts);
        Assert.True(ComposeRules.IsBlank(["", "", "", ""], string.Join('\n', Texts(seed)), seed.SignatureTexts, 0));
    }

    [Fact]
    public async Task Signature_settings_turn_it_off_or_move_it_above_the_quote()
    {
        var off = await PipelineRig.StartAsync(prefs: JsonNode.Parse("""{"signatureNewMailEnabled": false, "signatureReplyForwardEnabled": false}""")!.AsObject());
        Assert.Equal([""], Texts(await SeedAsync(off, ComposeKind.New)));
        Assert.DoesNotContain("-- ", Texts(await SeedAsync(off, ComposeKind.Reply, FakeSeed.AliasSubject)));

        var above = await PipelineRig.StartAsync(prefs: JsonNode.Parse("""{"signaturePosition": "above_quote", "signatureSeparatorEnabled": false}""")!.AsObject());
        var seed = await SeedAsync(above, ComposeKind.Reply, FakeSeed.AliasSubject);
        Assert.Equal(["", "Test User", "Example Ltd", "", "On Tue 10 Nov 2026, 11:00, Customer <customer@example.net> wrote:", "<quoted>"], Texts(seed));
    }

    [Theory]
    [InlineData(ComposeKind.Reply, "above_quote")]
    [InlineData(ComposeKind.Reply, "below_quote")]
    [InlineData(ComposeKind.ReplyAll, "above_quote")]
    [InlineData(ComposeKind.ReplyAll, "below_quote")]
    public async Task Quick_reply_text_is_plain_and_precedes_quote_without_changing_reply_fields(ComposeKind kind, string position)
    {
        var prefs = new JsonObject { ["signaturePosition"] = position };
        var rig = await PipelineRig.StartAsync(prefs: prefs, codec: new DocHtmlCodec());
        var request = new ComposeRequest(PipelineRig.Account, kind) { SourceEmailId = rig.Seeded(FakeSeed.ReplyToSubject) };
        var baseline = (await rig.Seeder.SeedAsync(request, Ct)).Value!;
        var result = await rig.Seeder.SeedAsync(request with { DraftText = "Hello <b>world</b>\n\nNext & last" }, Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        var seed = result.Value!;
        Assert.Equal(baseline.To, seed.To);
        Assert.Equal(baseline.Cc, seed.Cc);
        Assert.Equal(baseline.Bcc, seed.Bcc);
        Assert.Equal(baseline.Subject, seed.Subject);
        Assert.Equal(baseline.IdentityId, seed.IdentityId);
        Assert.Equal(baseline.FromOverride, seed.FromOverride);
        Assert.Equal(baseline.SourceEmailId, seed.SourceEmailId);
        Assert.Equal(baseline.InReplyTo, seed.InReplyTo);
        Assert.Equal(baseline.References, seed.References);
        Assert.Equal(baseline.SignatureTexts, seed.SignatureTexts);
        var texts = Texts(seed);
        Assert.Equal("Hello <b>world</b>", texts[0]);
        Assert.Equal("", texts[1]);
        Assert.Equal("Next & last", texts[2]);
        var quote = seed.Body.Blocks.ToList().FindIndex(b => b.Kind is BlockKind.Quoted);
        var signature = texts.ToList().IndexOf("-- ");
        Assert.True(quote > 2);
        Assert.True(position == "above_quote" ? signature > 2 && signature < quote : signature > quote);
        var html = DocHtml.Export(seed.Body);
        Assert.Contains("&lt;b&gt;world&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>world</b>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ComposeKind.New)]
    [InlineData(ComposeKind.Forward)]
    [InlineData(ComposeKind.ForwardAsAttachment)]
    [InlineData(ComposeKind.EditDraft)]
    public async Task Quick_reply_text_is_ignored_outside_reply_modes(ComposeKind kind)
    {
        var rig = await PipelineRig.StartAsync(codec: new DocHtmlCodec());
        var source = kind == ComposeKind.EditDraft
            ? (await rig.Drafts.SaveAsync(PipelineRig.NewDraft(), Ct)).Value!.DraftId
            : rig.Seeded(FakeSeed.InlineThreadSubject);
        var request = new ComposeRequest(PipelineRig.Account, kind) { SourceEmailId = source };
        var baseline = (await rig.Seeder.SeedAsync(request, Ct)).Value!;
        var seed = (await rig.Seeder.SeedAsync(request with { DraftText = "not for this composer" }, Ct)).Value!;
        Assert.Equal(DocHtml.Export(baseline.Body), DocHtml.Export(seed.Body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Empty_quick_reply_text_preserves_existing_reply_body(string? text)
    {
        var rig = await PipelineRig.StartAsync(codec: new DocHtmlCodec());
        var request = new ComposeRequest(PipelineRig.Account, ComposeKind.Reply) { SourceEmailId = rig.Seeded(FakeSeed.ReplyToSubject) };
        var baseline = (await rig.Seeder.SeedAsync(request, Ct)).Value!;
        var seed = (await rig.Seeder.SeedAsync(request with { DraftText = text }, Ct)).Value!;
        Assert.Equal(DocHtml.Export(baseline.Body), DocHtml.Export(seed.Body));
    }

    [Fact]
    public async Task A_prefill_from_a_mailto_link_fills_the_fields_and_the_body_above_the_signature()
    {
        var rig = await PipelineRig.StartAsync();
        var r = await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.New) { Prefill = new ComposePrefill { To = "x@example.net", Cc = "y@example.net", Subject = "Hi", Body = "Line one\nLine two" } }, Ct);
        Assert.Equal("x@example.net", r.Value!.To);
        Assert.Equal("y@example.net", r.Value.Cc);
        Assert.Equal("Hi", r.Value.Subject);
        Assert.Equal(["Line one", "Line two", "", "", "-- ", "Test User", "Example Ltd"], Texts(r.Value));
    }

    [Fact]
    public async Task A_reopened_draft_keeps_its_fields_identity_and_inline_images_with_their_blob_ids()
    {
        var rig = await PipelineRig.StartAsync();
        var png = rig.Store.PutBlob(FakeSeed.InlineImage);
        var pdf = rig.Store.PutBlob([9, 9]);
        var draft = PipelineRig.NewDraft(to: "Erin <erin@example.net>", subject: "Work in progress") with
        {
            Body = new Doc([Block.Para("see"), new Block(new BlockKind.Image(new ImageData { Src = "cid:pic@x", Cid = "pic@x", BlobId = png }))]),
            Attachments =
            [
                new DraftAttachment { Name = "pic.png", Type = "image/png", Size = FakeSeed.InlineImage.Length, BlobId = png, Cid = "pic@x" },
                new DraftAttachment { Name = "doc.pdf", Type = "application/pdf", Size = 2, BlobId = pdf },
            ],
            Cc = "carl@example.net",
        };
        var saved = (await rig.Drafts.SaveAsync(draft, Ct)).Value!;
        var seed = (await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.EditDraft) { SourceEmailId = saved.DraftId }, Ct)).Value!;
        Assert.Equal(saved.DraftId, seed.DraftId);
        Assert.True(seed.IsSaved);
        Assert.Equal("Erin <erin@example.net>", seed.To);
        Assert.Equal("carl@example.net", seed.Cc);
        Assert.Equal("Work in progress", seed.Subject);
        Assert.Equal("i1", seed.IdentityId);
        Assert.Equal(["pic.png", "doc.pdf"], seed.Attachments.Select(a => a.Name));
        Assert.Equal("pic@x", seed.Attachments[0].Cid);
        Assert.Null(seed.Attachments[1].Cid);
        var image = seed.Body.Blocks.Select(b => b.Kind).OfType<BlockKind.Image>().Single().Data;
        Assert.Equal("pic@x", image.Cid);
        Assert.NotNull(image.BlobId);
        Assert.Equal(FakeSeed.InlineImage, image.Bytes);
    }

    [Fact]
    public async Task A_draft_that_quotes_a_reply_reopens_with_the_quote_collapsed_again()
    {
        var rig = await PipelineRig.StartAsync();
        var reply = await SeedAsync(rig, ComposeKind.Reply, FakeSeed.AliasSubject);
        var saved = (await rig.Drafts.SaveAsync(new ComposeDraft { AccountId = PipelineRig.Account, IdentityId = "i1", To = reply.To, Subject = reply.Subject, Body = reply.Body }, Ct)).Value!;
        var seed = (await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.EditDraft) { SourceEmailId = saved.DraftId }, Ct)).Value!;
        Assert.Contains("<quoted>", Texts(seed));
    }

    [Fact]
    public async Task The_plugin_hooks_change_the_original_and_the_quote_header()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Hooks.Transform<EmailBody>(HookNames.OnBeforeComposeOpenToReply, (m, _) => ValueTask.FromResult(m with { Subject = "Plugin subject" }));
        rig.Hooks.Transform<QuoteHeaderPayload>(HookNames.OnBuildQuoteHeader, (h, _) => ValueTask.FromResult(h with { Header = "Dora said:" }));
        var seed = await SeedAsync(rig, ComposeKind.Reply, FakeSeed.AliasSubject);
        Assert.Equal("Re: Plugin subject", seed.Subject);
        Assert.Contains("Dora said:", Texts(seed));
    }

    [Fact]
    public async Task Edit_draft_hook_runs_before_a_saved_draft_opens()
    {
        var rig = await PipelineRig.StartAsync();
        var saved = (await rig.Drafts.SaveAsync(PipelineRig.NewDraft(subject: "Original"), Ct)).Value!;
        rig.Hooks.Transform<EmailBody>(HookNames.OnBeforeEditDraft, (m, _) => ValueTask.FromResult(m with { Subject = "Edited by plugin" }));
        var seed = (await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.EditDraft) { SourceEmailId = saved.DraftId }, Ct)).Value!;
        Assert.Equal("Edited by plugin", seed.Subject);
    }

    [Fact]
    public async Task Answering_nothing_is_an_error()
    {
        var rig = await PipelineRig.StartAsync();
        var r = await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.Reply), Ct);
        Assert.False(r.IsOk);
    }
}
