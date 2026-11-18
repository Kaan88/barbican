// M4 cal-data: the reminder popup view model over a fake scheduler: shown reminders come from Due, dismiss and snooze go to the scheduler, the missed list is loaded once and cleared on close (Rust reminders.rs:499 act, :523 missed_done).

using Barbican.Core.Calendar.Reminders;
using Barbican.Presentation.Calendar.Reminders;

namespace Barbican.Presentation.Tests.Calendar.Reminders;

public sealed class ReminderPopupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DueReminder Rem(string ev, long fire) => new($"{ev}:a:{fire}", ev, "a", fire, fire + 60_000, "T " + ev, "Work", false);

    private sealed class FakeScheduler : IReminderScheduler
    {
        public List<string> Dismissed { get; } = [];

        public List<(string Key, int Minutes)> Snoozed { get; } = [];

        public IReadOnlyList<DueReminder> MissedResult { get; set; } = [];

        public event EventHandler<ReminderEventArgs>? Due;

        public void Start()
        {
        }

        public void Shutdown()
        {
        }

        public Task DismissAsync(IReadOnlyList<DueReminder> reminders, CancellationToken ct)
        {
            Dismissed.AddRange(reminders.Select(r => r.Key));
            return Task.CompletedTask;
        }

        public void Snooze(IReadOnlyList<DueReminder> reminders, int minutes) => Snoozed.AddRange(reminders.Select(r => (r.Key, minutes)));

        public Task<IReadOnlyList<DueReminder>> MissedAsync(CancellationToken ct) => Task.FromResult(MissedResult);

        public void Raise(ReminderEventArgs e) => Due?.Invoke(this, e);
    }

    [Fact]
    public async Task Shown_reminders_come_from_due_soonest_first_and_dismiss_snooze_go_to_the_scheduler()
    {
        var sched = new FakeScheduler();
        using var popup = new ReminderPopup(sched);
        var changed = new List<string?>();
        popup.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var (a, b) = (Rem("a", 2_000_000), Rem("b", 1_000_000));
        sched.Raise(new ReminderEventArgs("k", [a, b], false));
        Assert.Equal("b,a", string.Join(',', popup.Shown.Select(d => d.Event)));
        Assert.Contains(nameof(popup.Shown), changed, StringComparer.Ordinal);

        popup.Snooze(b, 10);
        Assert.Equal(10, Assert.Single(sched.Snoozed).Minutes);
        Assert.Equal("a", Assert.Single(popup.Shown).Event);
        popup.Snooze(a, 0);
        Assert.Single(popup.Shown);

        await popup.DismissAsync(a, Ct);
        Assert.Empty(popup.Shown);
        Assert.Equal("a:a:2000000", Assert.Single(sched.Dismissed));
    }

    [Fact]
    public async Task Missed_reminders_are_listed_once_and_the_list_clears_on_close()
    {
        var sched = new FakeScheduler { MissedResult = [Rem("old", 5_000_000)] };
        using var popup = new ReminderPopup(sched);
        await popup.LoadMissedAsync(Ct);
        Assert.Equal("old", Assert.Single(popup.Missed).Event);
        await popup.CloseMissedAsync(false, Ct);
        Assert.Empty(popup.Missed);
    }
}
