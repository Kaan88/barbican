#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (as the ported Rust asserts)
using System.Text.Json.Nodes;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Tests.Compose;

/// <summary>Send with undo (HOLDFOR / FUTURERELEASE), schedule, undo, send now, the forbiddenFrom retry and the send hooks. J-061..J-066, B-COMP-007..013, I-282. FakeJmap-based.</summary>
public sealed class MailSenderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Envelope(PipelineRig rig, int index = 0) => rig.Api.Submissions[index]["envelope"]!.AsObject();

    [Fact]
    public async Task A_send_with_an_undo_window_holds_the_message_with_an_explicit_envelope_that_has_the_bcc()
    {
        var rig = await PipelineRig.StartAsync();
        var draft = PipelineRig.NewDraft() with { Cc = "cc@example.net", Bcc = "hidden@example.net" };
        var r = await rig.Sender.SendAsync(draft, new SendOptions { UndoSeconds = 5 }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.True(r.Value!.CanUndo);
        Assert.Equal(rig.Clock.GetUtcNow().AddSeconds(5), r.Value.ReleaseAt);
        var env = Envelope(rig);
        Assert.Equal("5", (string?)env["mailFrom"]!["parameters"]!["HOLDFOR"]);
        Assert.Equal(["erin@example.net", "cc@example.net", "hidden@example.net"], env["rcptTo"]!.AsArray().Select(x => (string?)x!["email"]));
        var state = Assert.Single(rig.Api.SubmissionStates);
        Assert.Equal("pending", state.UndoStatus);
    }

    [Fact]
    public async Task A_send_with_no_undo_window_holds_nothing_and_leaves_no_envelope()
    {
        var rig = await PipelineRig.StartAsync();
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 0 }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.False(r.Value!.CanUndo);
        Assert.Null(r.Value.ReleaseAt);
        Assert.Null(rig.Api.Submissions[0]["envelope"]);
        Assert.Equal("final", Assert.Single(rig.Api.SubmissionStates).UndoStatus);
    }

    [Fact]
    public async Task The_message_is_filed_in_sent_without_the_draft_keyword_and_the_replaced_draft_is_gone()
    {
        var rig = await PipelineRig.StartAsync();
        var saved = await rig.Drafts.SaveAsync(PipelineRig.NewDraft(), Ct);
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft() with { DraftId = saved.Value!.DraftId }, new SendOptions(), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Empty(rig.InFolder("drafts"));
        var sent = rig.Store.Email(r.Value!.EmailId)!;
        Assert.True(sent["mailboxIds"]!.AsObject().ContainsKey(rig.Store.MailboxId("sent")!));
        Assert.Null(sent["keywords"]!["$draft"]);
        Assert.Null(rig.Store.Email(saved.Value.DraftId));
    }

    [Fact]
    public async Task A_server_without_future_release_sends_at_once_and_cannot_undo()
    {
        var rig = await PipelineRig.StartAsync(PipelineRig.M3Options(futureRelease: false));
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 5 }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.False(r.Value!.CanUndo);
        Assert.Null(rig.Api.Submissions[0]["envelope"]);
        var undo = await rig.Sender.UndoAsync(r.Value, Ct);
        Assert.False(undo.IsOk);
    }

    [Fact]
    public async Task The_stalwart_thirty_days_are_clamped_to_seven_for_a_schedule()
    {
        var rig = await PipelineRig.StartAsync();
        var tooFar = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { ScheduledAt = rig.Clock.GetUtcNow().AddDays(10) }, Ct);
        Assert.False(tooFar.IsOk);
        Assert.Equal("This time is later than the server allows.", tooFar.Error!.Message);
        Assert.Empty(rig.Api.Submissions);
        var ok = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { ScheduledAt = rig.Clock.GetUtcNow().AddDays(6) }, Ct);
        Assert.True(ok.IsOk, ok.Error?.Message);
        Assert.Equal((6 * 86400).ToString(System.Globalization.CultureInfo.InvariantCulture), (string?)Envelope(rig)["mailFrom"]!["parameters"]!["HOLDFOR"]);
    }

    [Fact]
    public async Task The_undo_window_is_clamped_to_the_accounts_limit()
    {
        var rig = await PipelineRig.StartAsync(PipelineRig.M3Options(maxDelayedSend: 60));
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 300 }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("60", (string?)Envelope(rig)["mailFrom"]!["parameters"]!["HOLDFOR"]);
    }

    [Fact]
    public async Task Undo_cancels_the_hold_and_the_message_is_back_in_drafts_with_its_keyword()
    {
        var rig = await PipelineRig.StartAsync();
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(subject: "Oops"), new SendOptions { UndoSeconds = 10 }, Ct);
        var seed = await rig.Sender.UndoAsync(sent.Value!, Ct);
        Assert.True(seed.IsOk, seed.Error?.Message);
        Assert.Equal(ComposeKind.EditDraft, seed.Value!.Kind);
        Assert.Equal("Oops", seed.Value.Subject);
        Assert.Equal(sent.Value!.EmailId, seed.Value.DraftId);
        Assert.True(seed.Value.IsSaved);
        var draft = rig.Store.Email(Assert.Single(rig.InFolder("drafts")))!;
        Assert.True(draft["keywords"]!["$draft"]!.GetValue<bool>());
        Assert.DoesNotContain(rig.InFolder("sent"), id => (string?)rig.Store.Email(id)!["subject"] == "Oops");
        Assert.Empty(rig.Api.SubmissionStates);

        // the hold passes with nothing to release
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Empty(rig.Api.Released);
    }

    [Fact]
    public async Task Undo_after_the_release_says_the_message_has_gone()
    {
        var rig = await PipelineRig.StartAsync();
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 5 }, Ct);
        rig.Clock.Advance(TimeSpan.FromSeconds(6));
        var undo = await rig.Sender.UndoAsync(sent.Value!, Ct);
        Assert.False(undo.IsOk);
        Assert.Equal("The message has already been sent.", undo.Error!.Message);
        Assert.Equal([sent.Value!.EmailId], rig.Api.Released);
    }

    [Fact]
    public async Task Send_now_cancels_the_hold_and_submits_again_without_one()
    {
        var rig = await PipelineRig.StartAsync();
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 20 }, Ct);
        var now = await rig.Sender.SendNowAsync(sent.Value!, Ct);
        Assert.True(now.IsOk, now.Error?.Message);
        Assert.Equal("final", Assert.Single(rig.Api.SubmissionStates).UndoStatus);
        Assert.Equal(2, rig.Api.Submissions.Count);
        Assert.Null(rig.Api.Submissions[1]["envelope"]?["mailFrom"]?["parameters"]);
        Assert.Contains(sent.Value!.EmailId, rig.InFolder("sent"));

        // already gone: it says so
        var again = await rig.Sender.SendNowAsync(sent.Value, Ct);
        Assert.False(again.IsOk);
    }

    [Fact]
    public async Task A_refused_from_override_retries_once_with_the_identitys_address_and_says_so()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Api.ForbiddenMailFrom.Add("sales@example.org");
        var draft = PipelineRig.NewDraft() with { FromOverride = new FromOverride("Sales", "sales@example.org") };
        var r = await rig.Sender.SendAsync(draft, new SendOptions { UndoSeconds = 0 }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.True(r.Value!.EnvelopeRewritten);

        // the header From keeps the override; the accepted submission has no explicit envelope, so the server derived MAIL FROM from the identity
        var sent = rig.Store.Email(r.Value.EmailId)!;
        Assert.Equal("sales@example.org", (string?)sent["from"]![0]!["email"]);
        var accepted = Assert.Single(rig.Api.Submissions);
        Assert.Null(accepted["envelope"]);
    }

    [Fact]
    public async Task A_refusal_that_cannot_be_retried_takes_the_copy_out_again_and_leaves_no_stray_draft()
    {
        var rig = await PipelineRig.StartAsync();
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft(to: "someone@refused.example"), new SendOptions(), Ct);
        Assert.False(r.IsOk);
        Assert.Contains("recipients are invalid", r.Error!.Message, StringComparison.Ordinal);
        Assert.Empty(rig.InFolder("drafts"));
        Assert.DoesNotContain(rig.InFolder("sent"), id => (string?)rig.Store.Email(id)!["subject"] == "Hello");
    }

    [Fact]
    public async Task A_message_without_a_recipient_is_not_sent()
    {
        var rig = await PipelineRig.StartAsync();
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft(to: " "), new SendOptions(), Ct);
        Assert.False(r.IsOk);
        Assert.Equal("Add at least one recipient.", r.Error!.Message);
    }

    [Fact]
    public async Task Answering_and_forwarding_set_the_keywords_on_the_source()
    {
        var rig = await PipelineRig.StartAsync();
        var source = rig.Seeded(Barbican.FakeJmap.FakeSeed.AliasSubject);
        await rig.Sender.SendAsync(PipelineRig.NewDraft() with { Kind = ComposeKind.Reply, SourceEmailId = source }, new SendOptions(), Ct);
        Assert.True(rig.Store.Email(source)!["keywords"]!["$answered"]!.GetValue<bool>());
        await rig.Sender.SendAsync(PipelineRig.NewDraft() with { Kind = ComposeKind.Forward, SourceEmailId = source }, new SendOptions(), Ct);
        Assert.True(rig.Store.Email(source)!["keywords"]!["$forwarded"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Before_email_send_is_fail_closed_a_handler_that_throws_stops_the_send()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Hooks.Intercept<OutgoingEmailPayload>(HookNames.OnBeforeEmailSend, (_, _) => throw new InvalidOperationException("plugin bug"));
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions(), Ct);
        Assert.False(r.IsOk);
        Assert.Empty(rig.Api.Submissions);
        Assert.Empty(rig.InFolder("drafts"));
    }

    [Fact]
    public async Task Transform_rewrites_the_message_and_after_send_hears_of_it_with_the_schedule()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Hooks.Transform<OutgoingEmailPayload>(HookNames.OnTransformOutgoingEmail, (p, _) => ValueTask.FromResult(p with { Subject = p.Subject + " [x]", Bcc = "audit@example.net" }));
        var told = new TaskCompletionSource<EmailSentPayload>();
        rig.Hooks.Observe<EmailSentPayload>(HookNames.OnAfterEmailSend, (p, _) =>
        {
            told.TrySetResult(p);
            return ValueTask.CompletedTask;
        });
        var at = rig.Clock.GetUtcNow().AddHours(1);
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { ScheduledAt = at }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("Hello [x]", (string?)rig.Store.Email(r.Value!.EmailId)!["subject"]);
        Assert.Contains("audit@example.net", Envelope(rig)["rcptTo"]!.ToJsonString(), StringComparison.Ordinal);
        var payload = await told.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, Ct);
        Assert.Equal(r.Value.SubmissionId, payload.SubmissionId);
        Assert.Equal(at, payload.ScheduledFor);
    }

    [Fact]
    public async Task A_plugin_that_takes_over_sending_stops_ours()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Hooks.Intercept<ComposeSendRequestPayload>(HookNames.OnComposeSend, (_, _) => ValueTask.FromResult(false));
        var r = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions(), Ct);
        Assert.False(r.IsOk);
        Assert.Empty(rig.Api.Submissions);
    }
}
