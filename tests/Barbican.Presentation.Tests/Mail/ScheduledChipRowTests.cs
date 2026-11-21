// Bulwark components/email/thread-list-item.tsx:562-564,924-931 and lib/scheduled-send-label.ts: a message held for a scheduled send shows its
// release time in the date's place (a sky pill), a normal row keeps the date, and a submission that went out or was cancelled loses the chip.
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Compose.Pipeline;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class ScheduledChipRowTests
{
    [Fact]
    public void A_scheduled_row_names_its_release_time_in_the_date_s_place()
    {
        var tomorrow = new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);
        var context = new RowContext
        {
            Now = Mails.Now,
            Zone = TimeZoneInfo.Utc,
            ScheduledSends = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
            {
                ["today"] = Mails.Now.AddHours(3),
                ["tomorrow"] = tomorrow,
            },
        };

        var today = new MessageRowViewModel(Mails.Make("today"), context);
        Assert.True(today.ShowScheduledChip);
        Assert.Equal("15:00", today.ScheduledLabel);
        Assert.Equal("11:00", today.TimeLabel);

        // a release on another day carries its date, as Bulwark's formatScheduledSendAt does
        var other = new MessageRowViewModel(Mails.Make("tomorrow"), context);
        Assert.Equal("Wed 18 Nov, 08:00", other.ScheduledLabel);

        var plain = new MessageRowViewModel(Mails.Make("plain"), context);
        Assert.False(plain.ShowScheduledChip);
        Assert.Null(plain.ScheduledLabel);
        Assert.Equal("11:00", plain.TimeLabel);
    }

    [Fact]
    public void The_chip_follows_the_time_format_setting()
    {
        var context = new RowContext
        {
            Now = Mails.Now,
            Zone = TimeZoneInfo.Utc,
            TwelveHour = true,
            ScheduledSends = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { ["a"] = Mails.Now.AddHours(3) },
        };

        Assert.Equal("3:00 PM", new MessageRowViewModel(Mails.Make("a"), context).ScheduledLabel);
    }

    [Fact]
    public void A_row_whose_send_time_appears_or_goes_is_replaced()
    {
        var plain = new MessageRowViewModel(Mails.Make("a"), new RowContext { Now = Mails.Now, Zone = TimeZoneInfo.Utc });
        var scheduled = new MessageRowViewModel(Mails.Make("a"), new RowContext
        {
            Now = Mails.Now,
            Zone = TimeZoneInfo.Utc,
            ScheduledSends = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { ["a"] = Mails.Now.AddHours(3) },
        });
        var later = new MessageRowViewModel(Mails.Make("a"), new RowContext
        {
            Now = Mails.Now,
            Zone = TimeZoneInfo.Utc,
            ScheduledSends = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { ["a"] = Mails.Now.AddHours(5) },
        });

        Assert.False(plain.SameAs(scheduled));
        Assert.False(scheduled.SameAs(plain));
        Assert.False(scheduled.SameAs(later));
    }

    [Fact]
    public async Task The_sent_folder_shows_the_chip_for_a_pending_send_only()
    {
        var scheduled = new FakeScheduled();
        scheduled.Held.Add(new ScheduledMessage("m1", "s1", Mails.Now.AddHours(3)));
        using var h = new ListHarness(scheduled: scheduled);
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(Mails.Page(0, 3, 3, false)));

        await h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");

        var held = h.List.Messages.Single(r => string.Equals(r.Id, "m1", StringComparison.Ordinal));
        Assert.True(held.ShowScheduledChip);
        Assert.Equal("15:00", held.ScheduledLabel);
        Assert.All(h.List.Messages.Where(r => !string.Equals(r.Id, "m1", StringComparison.Ordinal)), r => Assert.False(r.ShowScheduledChip));
    }

    [Fact]
    public async Task A_send_that_went_out_or_was_cancelled_loses_its_chip()
    {
        var scheduled = new FakeScheduled();
        scheduled.Held.Add(new ScheduledMessage("m1", "s1", Mails.Now.AddHours(3)));
        using var h = new ListHarness(scheduled: scheduled);
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(Mails.Page(0, 3, 3, false)));
        await h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        Assert.True(h.List.Messages.Single(r => string.Equals(r.Id, "m1", StringComparison.Ordinal)).ShowScheduledChip);

        // the server no longer returns the submission: it went out (final) or the send was cancelled
        scheduled.Held.Clear();
        await h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        Assert.All(h.List.Messages, r => Assert.False(r.ShowScheduledChip));

        // and a push that touches the message takes the chip off without a folder change
        scheduled.Held.Add(new ScheduledMessage("m1", "s2", Mails.Now.AddHours(4)));
        await h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        Assert.True(h.List.Messages.Single(r => string.Equals(r.Id, "m1", StringComparison.Ordinal)).ShowScheduledChip);
        scheduled.Held.Clear();
        h.Hub.Publish(new EmailsChangedEvent(
            new Dictionary<string, EmailSummary>(StringComparer.Ordinal) { ["m1"] = Mails.Make("m1") with { Keywords = Keywords.Of("$seen", "$flagged") } },
            [],
            []));
        Assert.True(
            SpinWait.SpinUntil(() => h.List.Messages.All(r => !r.ShowScheduledChip), TimeSpan.FromSeconds(5)),
            "the chip stayed after the message changed: " + string.Join(",", h.List.Messages.Select(r => r.ScheduledLabel ?? "-").ToArray()));
    }

    [Fact]
    public async Task Another_folder_does_not_read_the_submissions()
    {
        var scheduled = new FakeScheduled();
        scheduled.Held.Add(new ScheduledMessage("m1", "s1", Mails.Now.AddHours(3)));
        var calls = 0;
        var counted = new CountingScheduled(scheduled, () => calls++);
        using var h = new ListHarness(scheduled: counted);
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(Mails.Page(0, 3, 3, false)));

        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");

        Assert.Equal(0, calls);
        Assert.All(h.List.Messages, r => Assert.False(r.ShowScheduledChip));
    }

    /// <summary>The pipeline fake with a call counter, so the test sees whether the list asked for submissions at all.</summary>
    private sealed class CountingScheduled(FakeScheduled inner, Action onList) : IScheduledSends
    {
        public long MaxDelayedSend(string accountId) => inner.MaxDelayedSend(accountId);

        public Task<Result<IReadOnlyList<ScheduledMessage>>> ListAsync(string accountId, CancellationToken ct)
        {
            onList();
            return inner.ListAsync(accountId, ct);
        }

        public Task<Result<Unit>> RescheduleAsync(string accountId, string submissionId, DateTimeOffset at, CancellationToken ct) =>
            inner.RescheduleAsync(accountId, submissionId, at, ct);

        public Task<Result<string>> CancelAsync(string accountId, string submissionId, CancellationToken ct) =>
            inner.CancelAsync(accountId, submissionId, ct);
    }
}
