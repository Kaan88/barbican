// Toasts (Bulwark lib/toast-duration.ts, lib/email-action-toast.ts): how long each kind stays, replacement of the same text, the stack limit, the announcement
// for Narrator. Owner actions.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.Presentation.Actions;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Actions;

public sealed class ToastServiceTests
{
    private static (ToastService Toasts, FakeTimeProvider Time) Make(string prefs = "{}")
    {
        var time = new FakeTimeProvider(Mails.Now);
        return (new ToastService(new TestPrefs(prefs), time, new Barbican.Presentation.Common.InlineDispatcher()), time);
    }

    [Fact]
    public void A_plain_toast_stays_for_the_setting_an_undo_toast_for_at_least_eight_seconds_an_error_for_ten()
    {
        var (toasts, time) = Make();
        toasts.Show(new ToastRequest("plain"));
        toasts.Show(new ToastRequest("with undo", ActionLabel: "Undo", Action: _ => Task.CompletedTask));
        toasts.Show(new ToastRequest("bad", IsError: true));
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(["with undo", "bad"], toasts.Toasts.Select(t => t.Message));
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(["bad"], toasts.Toasts.Select(t => t.Message));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(toasts.Toasts);
    }

    [Fact]
    public void A_toast_shown_from_a_pool_thread_is_added_on_the_ui_thread_not_on_the_caller()
    {
        var queue = new Queue<Action>();
        var toasts = new ToastService(new TestPrefs("{}"), new FakeTimeProvider(Mails.Now), new OffThreadDispatcher(queue));
        var handle = toasts.Show(new ToastRequest("from a send"));
        Assert.Empty(toasts.Toasts);
        queue.Dequeue()();
        Assert.Equal(["from a send"], toasts.Toasts.Select(t => t.Message));
        Assert.Equal(handle, toasts.Toasts[0].Handle);
    }

    private sealed class OffThreadDispatcher(Queue<Action> queue) : Barbican.Presentation.Common.IDispatcher
    {
        public bool HasThreadAccess => false;

        public void Post(Action action) => queue.Enqueue(action);

        public Task InvokeAsync(Func<Task> action) => action();
    }

    [Fact]
    public void A_duration_of_zero_keeps_toasts_until_they_are_closed()
    {
        var (toasts, time) = Make("""{"toastDuration": 0}""");
        var handle = toasts.Show(new ToastRequest("stays"));
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Single(toasts.Toasts);
        toasts.Dismiss(handle);
        Assert.Empty(toasts.Toasts);
    }

    [Fact]
    public void The_same_words_replace_the_toast_and_only_three_are_kept()
    {
        var (toasts, _) = Make();
        toasts.Show(new ToastRequest("a"));
        toasts.Show(new ToastRequest("a"));
        Assert.Single(toasts.Toasts);
        toasts.Show(new ToastRequest("b"));
        toasts.Show(new ToastRequest("c"));
        toasts.Show(new ToastRequest("d"));
        Assert.Equal(["b", "c", "d"], toasts.Toasts.Select(t => t.Message));
    }

    [Fact]
    public void Every_toast_is_announced()
    {
        var (toasts, _) = Make();
        var heard = new List<string>();
        toasts.Announced += (_, e) => heard.Add(e.Toast.Message);
        toasts.Show(new ToastRequest("Moved to Archive"));
        toasts.Show(new ToastRequest("Could not move", IsError: true));
        Assert.Equal(["Moved to Archive", "Could not move"], heard);
    }
}
