#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Compose;
using Barbican.Presentation.Compose.Pipeline;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Compose.Pipeline;

/// <summary>The schedule dialog (I-241) and the Scheduled view: presets, typed date and time with the checks, Reschedule, the banner and its actions.</summary>
public sealed class ScheduleDialogViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Monday 2026-11-16 07:00 UTC (the test clock's zone is UTC)
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 16, 7, 0, 0, TimeSpan.Zero));

    [Fact]
    public void The_dialog_offers_the_presets_with_their_times_and_defaults_to_tomorrow_morning()
    {
        var vm = new ScheduleDialogViewModel(ScheduleMode.Compose, Clock(), 7 * 86400, false);
        Assert.Equal("Schedule send", vm.Title);
        Assert.Equal(["This morning", "This afternoon", "Tomorrow morning", "Tomorrow afternoon", "Monday morning"], vm.Presets.Select(p => p.Label));
        Assert.Equal("Tue 17 Nov, 08:00", vm.Presets[2].When);
        Assert.Equal("2026-11-17", vm.Date);
        Assert.Equal("08:00", vm.Time);
        Assert.Equal("", vm.Error);
        Assert.Equal("Tue 17 Nov, 1:00 PM", new ScheduleDialogViewModel(ScheduleMode.Compose, Clock(), 7 * 86400, true).Presets[3].When);
    }

    [Fact]
    public async Task A_preset_closes_the_dialog_with_its_time()
    {
        var vm = new ScheduleDialogViewModel(ScheduleMode.Compose, Clock(), 7 * 86400, false);
        var chosen = vm.Choose(2);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero), chosen);
        Assert.Equal(chosen, await vm.Completion);
    }

    [Fact]
    public async Task Typed_times_are_checked_with_the_dialogs_words()
    {
        var vm = new ScheduleDialogViewModel(ScheduleMode.Compose, Clock(), 7 * 86400, false) { Date = "2026-11-16", Time = "06:00" };
        Assert.Null(vm.ChooseTyped());
        Assert.Equal("Choose a future date and time.", vm.Error);
        vm.Date = "2026-12-30";
        vm.Time = "08:00";
        Assert.Null(vm.ChooseTyped());
        Assert.Equal("This time is later than the server allows.", vm.Error);
        vm.Time = "25:00";
        Assert.Null(vm.ChooseTyped());
        Assert.Equal("Enter a valid date and time.", vm.Error);
        vm.Date = "17.11.2026";
        vm.Time = "8:30 pm";
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 20, 30, 0, TimeSpan.Zero), vm.ChooseTyped());
        Assert.Equal("", vm.Error);
        Assert.NotNull(await vm.Completion);
    }

    [Fact]
    public async Task A_server_that_cannot_hold_says_so_up_front_and_cancel_completes_with_nothing()
    {
        var vm = new ScheduleDialogViewModel(ScheduleMode.Compose, Clock(), 0, false);
        Assert.Equal("Scheduled send is not supported for this account.", vm.Error);
        Assert.Null(vm.Choose(0));
        vm.Cancel();
        Assert.Null(await vm.Completion);
    }

    [Fact]
    public void Reschedule_starts_at_the_messages_own_time_while_it_is_still_ahead()
    {
        var own = new DateTimeOffset(2026, 11, 20, 14, 30, 0, TimeSpan.Zero);
        var vm = new ScheduleDialogViewModel(ScheduleMode.Reschedule, Clock(), 7 * 86400, false, own);
        Assert.Equal("Reschedule", vm.Title);
        Assert.Equal("2026-11-20", vm.Date);
        Assert.Equal("14:30", vm.Time);
        var past = new ScheduleDialogViewModel(ScheduleMode.Reschedule, Clock(), 7 * 86400, false, own.AddDays(-10));
        Assert.Equal("2026-11-17", past.Date);
    }

    [Fact]
    public void Typed_time_uses_selected_zone_and_rejects_the_dst_gap()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var vm = new ScheduleDialogViewModel(ScheduleMode.Compose, clock, 7 * 86400, false, zone: zone) { Date = "2026-03-29", Time = "02:30" };
        Assert.Null(vm.ChooseTyped());
        vm.Time = "03:30";
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), vm.ChooseTyped());
        Assert.Equal(TimeZoneInfo.Utc, clock.LocalTimeZone);
    }

    private sealed class Host(DateTimeOffset? answer) : IScheduleDialogHost
    {
        public ScheduleDialogViewModel? Shown { get; private set; }

        public Task<DateTimeOffset?> ShowAsync(ScheduleDialogViewModel dialog, CancellationToken ct)
        {
            Shown = dialog;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task The_banner_names_the_release_time_for_a_scheduled_message_only()
    {
        var clock = Clock();
        var scheduled = new FakeScheduled();
        scheduled.Held.Add(new ScheduledMessage("e1", "s1", new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero)));
        var source = new ScheduledBannerSource(scheduled, new FakeActiveMail(), new TestPrefs(), clock);
        var banners = await source.BannersForAsync(new Barbican.Presentation.Mail.ReaderSnapshot { EmailId = "e1", MailboxRoles = ["sent"] }, Ct).ToListAsync(Ct);
        var banner = Assert.Single(banners);
        Assert.Equal("Scheduled to send at Tue 17 Nov, 08:00", banner.Title);
        Assert.Equal(["Reschedule", "Cancel and edit", "Cancel send"], banner.Actions.Select(a => a.Label));

        Assert.Empty(await source.BannersForAsync(new Barbican.Presentation.Mail.ReaderSnapshot { EmailId = "other", MailboxRoles = ["sent"] }, Ct).ToListAsync(Ct));
        Assert.Empty(await source.BannersForAsync(new Barbican.Presentation.Mail.ReaderSnapshot { EmailId = "e1", MailboxRoles = ["inbox"] }, Ct).ToListAsync(Ct));
    }

    [Fact]
    public async Task The_banner_actions_reschedule_cancel_and_edit_or_cancel()
    {
        var clock = Clock();
        var scheduled = new FakeScheduled();
        scheduled.Held.Add(new ScheduledMessage("e1", "s1", new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero)));
        var toasts = new RecordingToasts();
        var composer = new FakeComposer();
        var newTime = new DateTimeOffset(2026, 11, 18, 13, 0, 0, TimeSpan.Zero);
        var host = new Host(newTime);
        var actions = new ScheduledBannerActions(scheduled, new FakeActiveMail(), host, composer, toasts, new TestPrefs(), clock);

        Assert.Equal("Rescheduled for Wed 18 Nov, 13:00.", await actions.RunAsync("e1", ScheduledBannerSource.Reschedule, Ct));
        Assert.Equal([("s1", newTime)], scheduled.Rescheduled);
        Assert.Equal("Reschedule", host.Shown!.Title);
        Assert.Equal("2026-11-17", host.Shown.Date);

        await actions.RunAsync("e1", ScheduledBannerSource.CancelAndEdit, Ct);
        Assert.Equal("e1", Assert.Single(composer.Opened).SourceEmailId);

        await actions.RunAsync("e1", ScheduledBannerSource.CancelSend, Ct);
        Assert.Equal(["s1", "s1"], scheduled.Cancelled);
        Assert.Equal("Open", toasts.Shown[^1].ActionLabel);

        // the reader hands these ids to the registered handlers (UI sweep S-01: Cancel send answered "Unknown action")
        Assert.True(actions.Owns(ScheduledBannerSource.CancelSend));
        Assert.True(actions.Owns(ScheduledBannerSource.Reschedule));
        Assert.True(actions.Owns(ScheduledBannerSource.CancelAndEdit));
        Assert.False(actions.Owns("draft.edit"));

        Assert.Equal("This message is no longer scheduled.", await actions.RunAsync("gone", ScheduledBannerSource.CancelSend, Ct));
    }
}
