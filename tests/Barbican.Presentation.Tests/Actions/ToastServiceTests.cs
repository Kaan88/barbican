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
    public void Replacement_respects_source_identity_and_effective_severity()
    {
        var (toasts, _) = Make();
        var first = toasts.Show(new ToastRequest("shared") { Source = "Add-on", SourceId = "a", Severity = ToastSeverity.Warning });
        var second = toasts.Show(new ToastRequest("shared") { Source = "Add-on", SourceId = "b", Severity = ToastSeverity.Warning });
        var core = toasts.Show(new ToastRequest("shared"));
        Assert.Equal(3, toasts.Toasts.Count);
        var replacement = toasts.Show(new ToastRequest("shared") { Source = "Add-on", SourceId = "a", Severity = ToastSeverity.Warning });
        Assert.Equal([second, core, replacement], toasts.Toasts.Select(t => t.Handle));
        Assert.DoesNotContain(toasts.Toasts, t => t.Handle == first);
        toasts.Dismiss(second);
        toasts.Show(new ToastRequest("shared") { Source = "Add-on", SourceId = "a", Severity = ToastSeverity.Success });
        Assert.Contains(toasts.Toasts, t => t.Handle == replacement);
        Assert.Equal(3, toasts.Toasts.Count);
    }

    [Fact]
    public void Attribution_is_host_framed_for_visual_and_accessible_text()
    {
        var (toasts, _) = Make();
        toasts.Show(new ToastRequest("Done") { Source = "Sample", SourceId = "sample" });
        var item = Assert.Single(toasts.Toasts);
        Assert.Equal("Sample", item.Source);
        Assert.Equal("Add-on: Sample (sample)", item.DisplaySource);
        Assert.Equal("Add-on: Sample (sample): Done", item.AccessibleName);
        toasts.Show(new ToastRequest("Core"));
        Assert.Null(toasts.LastShown!.DisplaySource);
        Assert.Equal("Core", toasts.LastShown.AccessibleName);
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
