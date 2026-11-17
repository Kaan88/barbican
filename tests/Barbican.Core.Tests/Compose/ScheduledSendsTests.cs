#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (as the ported Rust asserts)
using System.Text.Json.Nodes;
using Barbican.Core.Compose;

namespace Barbican.Core.Tests.Compose;

/// <summary>The Scheduled view's data and actions: list pending submissions, reschedule (with the fallbacks of ReschedulePlanner, B-JMAP-038/039), cancel. J-066, L-002..L-006. FakeJmap-based.</summary>
public sealed class ScheduledSendsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(PipelineRig Rig, SendReceipt Receipt, DateTimeOffset At)> ScheduleAsync(string subject = "Later")
    {
        var rig = await PipelineRig.StartAsync();
        var at = rig.Clock.GetUtcNow().AddHours(1);
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(subject: subject), new SendOptions { ScheduledAt = at }, Ct);
        Assert.True(sent.IsOk, sent.Error?.Message);
        return (rig, sent.Value!, at);
    }

    [Fact]
    public async Task The_scheduled_list_has_the_pending_messages_with_their_release_time()
    {
        var (rig, receipt, at) = await ScheduleAsync();
        var list = await rig.Scheduled.ListAsync(PipelineRig.Account, Ct);
        Assert.True(list.IsOk, list.Error?.Message);
        var one = Assert.Single(list.Value!);
        Assert.Equal(receipt.EmailId, one.EmailId);
        Assert.Equal(receipt.SubmissionId, one.SubmissionId);
        Assert.Equal(at, one.At);

        // once released it is no longer scheduled
        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Empty((await rig.Scheduled.ListAsync(PipelineRig.Account, Ct)).Value!);
    }

    [Fact]
    public async Task Reschedule_moves_the_release_time_and_keeps_the_envelope()
    {
        var (rig, receipt, _) = await ScheduleAsync();
        var later = rig.Clock.GetUtcNow().AddHours(3);
        var r = await rig.Scheduled.RescheduleAsync(PipelineRig.Account, receipt.SubmissionId, later, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        var one = Assert.Single((await rig.Scheduled.ListAsync(PipelineRig.Account, Ct)).Value!);
        Assert.Equal(later, one.At);
        Assert.NotEqual(receipt.SubmissionId, one.SubmissionId);
        Assert.Equal("erin@example.net", (string?)rig.Api.Submissions[^1]["envelope"]!["rcptTo"]![0]!["email"]);
    }

    [Fact]
    public async Task Reschedule_refuses_a_time_the_server_would_not_hold_before_it_touches_anything()
    {
        var (rig, receipt, at) = await ScheduleAsync();
        var r = await rig.Scheduled.RescheduleAsync(PipelineRig.Account, receipt.SubmissionId, rig.Clock.GetUtcNow().AddDays(20), Ct);
        Assert.False(r.IsOk);
        Assert.Equal(at, Assert.Single((await rig.Scheduled.ListAsync(PipelineRig.Account, Ct)).Value!).At);
    }

    [Fact]
    public async Task Reschedule_falls_back_to_restoring_the_draft_when_the_recreate_fails()
    {
        var (rig, receipt, _) = await ScheduleAsync("Stuck");

        // from now on the server refuses the sender address: the new submission and the one at the old time both fail
        rig.Api.ForbiddenMailFrom.Add("user@example.org");
        var r = await rig.Scheduled.RescheduleAsync(PipelineRig.Account, receipt.SubmissionId, rig.Clock.GetUtcNow().AddHours(4), Ct);
        Assert.False(r.IsOk);
        Assert.Contains("The message is back in Drafts.", r.Error!.Message, StringComparison.Ordinal);
        var draft = rig.Store.Email(Assert.Single(rig.InFolder("drafts")))!;
        Assert.Equal("Stuck", (string?)draft["subject"]);
        Assert.True(draft["keywords"]!["$draft"]!.GetValue<bool>());
        Assert.Empty((await rig.Scheduled.ListAsync(PipelineRig.Account, Ct)).Value!);
    }

    [Fact]
    public async Task Reschedule_of_a_message_already_sent_says_so()
    {
        var (rig, receipt, _) = await ScheduleAsync();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        var r = await rig.Scheduled.RescheduleAsync(PipelineRig.Account, receipt.SubmissionId, rig.Clock.GetUtcNow().AddHours(1), Ct);
        Assert.False(r.IsOk);
        Assert.Equal("This message was already sent.", r.Error!.Message);
    }

    [Fact]
    public async Task Cancel_returns_the_message_to_drafts_and_empties_the_list()
    {
        var (rig, receipt, _) = await ScheduleAsync("Changed my mind");
        var r = await rig.Scheduled.CancelAsync(PipelineRig.Account, receipt.SubmissionId, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal(receipt.EmailId, r.Value);
        Assert.Equal(receipt.EmailId, Assert.Single(rig.InFolder("drafts")));
        Assert.Empty((await rig.Scheduled.ListAsync(PipelineRig.Account, Ct)).Value!);
        Assert.Empty(rig.Api.Released);
    }

    [Fact]
    public async Task Cancel_of_a_sent_message_says_it_cannot_be_unsent()
    {
        var (rig, receipt, _) = await ScheduleAsync();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        var r = await rig.Scheduled.CancelAsync(PipelineRig.Account, receipt.SubmissionId, Ct);
        Assert.False(r.IsOk);
        _ = (JsonNode?)null;
        Assert.Contains("already been sent", r.Error!.Message, StringComparison.Ordinal);
    }
}
