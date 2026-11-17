#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Presentation.Compose.Pipeline;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Compose.Pipeline;

/// <summary>The send flow between the composer and IMailSender: checks, the warnings dialog, the undo-send toast with Undo and Send now, the scheduled toast with Cancel (I-282, G-054, B-COMP-007..013).</summary>
public sealed class SendCoordinatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Rig
    {
        public Rig(string prefs = "{}")
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
            Coordinator = new SendCoordinator(Sender, Scheduled, new FakeIdentities("me@own.example"), new TestPrefs(prefs), Toasts, Composer, Clock);
        }

        public FakeTimeProvider Clock { get; }

        public FakeSender Sender { get; } = new();

        public FakeScheduled Scheduled { get; } = new();

        public RecordingToasts Toasts { get; } = new();

        public FakeComposer Composer { get; } = new();

        public SendCoordinator Coordinator { get; }
    }

    private static ComposeDraft Draft(string to = "friend@own.example", string subject = "Hello", string body = "Hi") => new()
    {
        AccountId = "acct",
        IdentityId = "i0",
        To = to,
        Subject = subject,
        Body = new Doc([Block.Para(body)]),
    };

    [Fact]
    public async Task A_send_uses_the_undo_window_of_the_settings_and_offers_undo_and_send_now()
    {
        var rig = new Rig("""{"sendDelaySeconds": 10}""");
        rig.Sender.Receipt = new SendReceipt("acct", "e1", "s1") { CanUndo = true, ReleaseAt = rig.Clock.GetUtcNow().AddSeconds(10) };
        var outcome = await rig.Coordinator.RunAsync(Draft(), SendMode.Now, Ct);
        Assert.Equal(SendOutcomeStatus.Sent, outcome.Status);
        Assert.Equal(10, rig.Sender.Sent[0].Options.UndoSeconds);
        var toast = Assert.Single(rig.Toasts.Shown);
        Assert.Equal("Undo", toast.ActionLabel);
        Assert.Equal("Send now", toast.SecondaryLabel);
        Assert.Equal(TimeSpan.FromSeconds(10), toast.Duration);

        await toast.Action!(Ct);
        Assert.Single(rig.Sender.Undone);
        Assert.Equal("e1", Assert.Single(rig.Composer.Seeds).DraftId);

        await toast.SecondaryAction!(Ct);
        Assert.Single(rig.Sender.Released);
        Assert.Equal("Message sent.", rig.Toasts.Shown[^1].Message);
    }

    [Fact]
    public async Task A_send_with_no_hold_says_sent_without_undo()
    {
        var rig = new Rig();
        await rig.Coordinator.RunAsync(Draft(), SendMode.Now, Ct);
        var toast = Assert.Single(rig.Toasts.Shown);
        Assert.Equal("Message sent.", toast.Message);
        Assert.Null(toast.ActionLabel);
    }

    [Fact]
    public async Task A_refusal_is_an_error_toast_and_a_failed_outcome()
    {
        var rig = new Rig();
        rig.Sender.Failure = "Your mailbox is full.";
        var outcome = await rig.Coordinator.RunAsync(Draft(), SendMode.Now, Ct);
        Assert.Equal(SendOutcomeStatus.Failed, outcome.Status);
        Assert.Equal("Your mailbox is full.", outcome.Message);
        Assert.True(Assert.Single(rig.Toasts.Shown).IsError);
        Assert.False(rig.Coordinator.IsSending);
    }

    [Fact]
    public async Task Warnings_wait_for_the_dialog_send_anyway_sends_and_cancel_does_not()
    {
        var rig = new Rig();
        IReadOnlyList<SendWarning>? seen = null;
        rig.Coordinator.WarningsRaised += (_, e) => seen = e.Warnings;
        var pending = rig.Coordinator.RunAsync(Draft(to: "x@other.example", subject: ""), SendMode.Now, Ct);
        await Task.Delay(50, Ct);
        Assert.False(pending.IsCompleted);
        Assert.Equal([SendWarningKind.EmptySubject, SendWarningKind.ExternalRecipients], seen!.Select(w => w.Kind));
        Assert.Equal(["x@other.example"], seen![1].Items);
        rig.Coordinator.Confirm();
        Assert.Equal(SendOutcomeStatus.Sent, (await pending).Status);
        Assert.Single(rig.Sender.Sent);

        var again = rig.Coordinator.RunAsync(Draft(subject: ""), SendMode.Now, Ct);
        await Task.Delay(50, Ct);
        rig.Coordinator.Cancel();
        Assert.Equal(SendOutcomeStatus.Cancelled, (await again).Status);
        Assert.Single(rig.Sender.Sent);
    }

    [Fact]
    public async Task A_second_send_while_one_waits_is_busy()
    {
        var rig = new Rig();
        rig.Coordinator.WarningsRaised += (_, _) => { };
        var first = rig.Coordinator.RunAsync(Draft(subject: ""), SendMode.Now, Ct);
        await Task.Delay(50, Ct);
        Assert.True(rig.Coordinator.IsSending);
        Assert.Equal(SendOutcomeStatus.Busy, (await rig.Coordinator.RunAsync(Draft(), SendMode.Now, Ct)).Status);
        rig.Coordinator.Cancel();
        await first;
    }

    [Fact]
    public async Task Warnings_follow_their_settings_and_nobody_to_ask_means_the_send_goes_on()
    {
        var rig = new Rig("""{"emptySubjectWarningEnabled": false}""");
        rig.Coordinator.WarningsRaised += (_, _) => throw new InvalidOperationException("no warning expected");
        Assert.Equal(SendOutcomeStatus.Sent, (await rig.Coordinator.RunAsync(Draft(subject: ""), SendMode.Now, Ct)).Status);

        var headless = new Rig();
        Assert.Equal(SendOutcomeStatus.Sent, (await headless.Coordinator.RunAsync(Draft(subject: ""), SendMode.Now, Ct)).Status);
    }

    [Fact]
    public async Task Pending_images_bad_addresses_and_no_recipient_stop_before_the_sender()
    {
        var rig = new Rig();
        var pendingImage = Draft() with { Body = new Doc([Block.Para("x"), new Block(new BlockKind.Image(new ImageData { Src = "cid:a", Cid = "a" }))]) };
        Assert.Equal(SendOutcomeStatus.PendingImages, (await rig.Coordinator.RunAsync(pendingImage, SendMode.Now, Ct)).Status);

        var bad = await rig.Coordinator.RunAsync(Draft(to: "friend@own.example, not an address"), SendMode.Now, Ct);
        Assert.Equal(SendOutcomeStatus.InvalidAddresses, bad.Status);
        Assert.Equal(["not an address"], bad.InvalidAddresses);

        Assert.Equal(SendOutcomeStatus.Failed, (await rig.Coordinator.RunAsync(Draft(to: " "), SendMode.Now, Ct)).Status);
        Assert.Empty(rig.Sender.Sent);
    }

    [Fact]
    public async Task A_scheduled_send_toasts_its_time_and_cancel_moves_it_to_drafts_with_an_open_button()
    {
        var rig = new Rig();
        var at = rig.Clock.GetUtcNow().AddDays(1).AddHours(-1);
        rig.Scheduled.Held.Add(new ScheduledMessage("e1", "s1", at));
        var outcome = await rig.Coordinator.RunAsync(Draft(), SendMode.At(at), Ct);
        Assert.Equal(SendOutcomeStatus.Scheduled, outcome.Status);
        Assert.Equal(at, rig.Sender.Sent[0].Options.ScheduledAt);
        var toast = Assert.Single(rig.Toasts.Shown);
        Assert.Equal("Scheduled for Wed 18 Nov, 08:00.", toast.Message);
        Assert.Equal("Cancel", toast.ActionLabel);

        await toast.Action!(Ct);
        Assert.Equal(["s1"], rig.Scheduled.Cancelled);
        var opened = rig.Toasts.Shown[^1];
        Assert.Equal("Open", opened.ActionLabel);
        await opened.Action!(Ct);
        var request = Assert.Single(rig.Composer.Opened);
        Assert.Equal(ComposeKind.EditDraft, request.Kind);
        Assert.Equal("e1", request.SourceEmailId);
    }
}
