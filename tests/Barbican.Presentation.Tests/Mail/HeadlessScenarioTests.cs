// The mailcore headless scenario (M1-plan §6.2): open the Inbox, j x20 at 33 ms, a push arrives, load more. No window: it asserts the collection
// notification counts (one Reset for the first fill, none after) and the UI-thread work per step. Runs on Windows and, through tools/linux.ps1 test,
// on the VM. Owner mailcore.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using System.Collections.Specialized;
using System.Diagnostics;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Input;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class HeadlessScenarioTests
{
    /// <summary>Generous on purpose: a shared CI-less VM is slower than the dev machine; the bar the scenario guards is "no per-step full rebuild".</summary>
    private static readonly TimeSpan StepBudget = TimeSpan.FromMilliseconds(25);

    [Fact]
    public async Task Open_inbox_hold_j_push_and_load_more_without_a_reset_or_a_slow_step()
    {
        using var h = new ListHarness();
        h.Service.OnQuery = (_, after, _) => Task.FromResult(Result<ListPage>.Ok(after is null ? Mails.Page(0, 50, 400, true) : Mails.Page(50, 50, 400, true)));
        var changes = new List<(string Step, NotifyCollectionChangedAction Action)>();
        var step = "open";
        h.List.Rows.CollectionChanged += (_, e) => changes.Add((step, e.Action));
        var times = new List<(string Step, double Ms)>();

        double Measure(Action what)
        {
            var sw = Stopwatch.StartNew();
            what();
            return sw.Elapsed.TotalMilliseconds;
        }

        var sw0 = Stopwatch.StartNew();
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        times.Add(("open", sw0.Elapsed.TotalMilliseconds));

        step = "j";
        for (var i = 0; i < 20; i++)
        {
            times.Add(("j", Measure(() => Assert.True(h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard)))));
            h.Time.Advance(TimeSpan.FromMilliseconds(33));
        }

        Assert.Equal("m19", h.List.Selected?.Id);
        Assert.True(h.Motion.Rapid);

        step = "push";
        var fresh = Mails.Make("pushed", unread: true, hoursAgo: 0.01);
        times.Add(("push", Measure(() => h.Hub.Publish(new ListDeltaEvent(new ListDelta { Added = [("pushed", 0)], Emails = new Dictionary<string, EmailSummary> { ["pushed"] = fresh }, Total = 401 })))));
        Assert.Equal("m19", h.List.Selected?.Id);

        step = "more";
        var sw1 = Stopwatch.StartNew();
        await h.List.LoadMoreAsync();
        times.Add(("more", sw1.Elapsed.TotalMilliseconds));

        Assert.Equal(101, h.List.Messages.Count());
        Assert.Equal(1, changes.Count(c => c.Action == NotifyCollectionChangedAction.Reset));
        Assert.Equal("open", changes.Single(c => c.Action == NotifyCollectionChangedAction.Reset).Step);
        Assert.DoesNotContain(changes, c => string.Equals(c.Step, "push", StringComparison.Ordinal) && c.Action != NotifyCollectionChangedAction.Add);

        var jMax = times.Where(t => string.Equals(t.Step, "j", StringComparison.Ordinal)).Max(t => t.Ms);
        var jMedian = times.Where(t => string.Equals(t.Step, "j", StringComparison.Ordinal)).Select(t => t.Ms).Order().ElementAt(10);
        var report = (
            $"headless scenario: open {times[0].Ms:0.00} ms, j median {jMedian:0.000} ms max {jMax:0.000} ms, push {times.First(t => string.Equals(t.Step, "push", StringComparison.Ordinal)).Ms:0.000} ms, load more {times[^1].Ms:0.00} ms; notifications {string.Join(", ", changes.GroupBy(c => c.Step + ":" + c.Action).Select(g => g.Key + "=" + g.Count()))}");
        if (Environment.GetEnvironmentVariable("BARBICAN_SCENARIO_OUT") is { Length: > 0 } outPath)
        {
            await File.AppendAllTextAsync(outPath, report + Environment.NewLine, TestContext.Current.CancellationToken);
        }

        Assert.True(jMax < StepBudget.TotalMilliseconds * 4, $"a j step took {jMax:0.0} ms");
        Assert.True(jMedian < StepBudget.TotalMilliseconds, $"the median j step took {jMedian:0.0} ms");
    }
}
