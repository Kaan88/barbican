// M2 plan §6.3 step 10 and §1 rule 8 (review of the reader stream): the banners of the sources that answer within ReaderDepth.BannerBudget go
// with the page in the reveal's one Apply; a slower source (an invitation waits on CalendarEvent/parse) never holds the message back: its banner
// goes with the page when it arrives before the reveal, else it joins the stack of the message on screen afterwards. Owner reader.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using System.Runtime.CompilerServices;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Tests.Mail.Loading;

public sealed class BannerBudgetTests
{
    [Fact]
    public void A_fast_banner_is_applied_with_the_page_in_one_apply()
    {
        var depth = new ReaderDepth(null, [new Source("fast", null)], null);
        using var h = new LoaderHarness(depth: depth);
        h.Cached("m1");

        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);

        Assert.True(SpinUntil(() => string.Equals(h.Presenter.Shown, "m1", StringComparison.Ordinal)));
        var ready = Assert.Single(h.Applies, a => a.BodyState == BodyState.Ready);
        Assert.Equal(["fast"], ready.Banners.Select(b => b.Id));
        Assert.Equal(["fast"], h.Reader.Banners.Select(b => b.Id));
    }

    [Fact]
    public void A_slow_banner_does_not_hold_the_page_and_joins_the_stack_after_the_reveal()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var depth = new ReaderDepth(null, [new Source("slow", gate.Task), new Source("fast", null)], null);
        using var h = new LoaderHarness(depth: depth);
        h.Cached("m1");

        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        Assert.Null(h.Presenter.Shown);

        // the budget runs out: the page goes with what is there
        h.Time.Advance(ReaderDepth.BannerBudget);
        Assert.True(SpinUntil(() => string.Equals(h.Presenter.Shown, "m1", StringComparison.Ordinal)));
        Assert.Equal(["fast"], h.Reader.Banners.Select(b => b.Id));

        gate.SetResult();
        Assert.True(SpinUntil(() => h.Reader.Banners.Count == 2));
        Assert.Equal(["fast", "slow"], h.Reader.Banners.Select(b => b.Id));
        Assert.Equal("m1", h.Reader.EmailId);
    }

    [Fact]
    public async Task A_slow_banner_that_arrives_before_the_reveal_goes_with_the_page()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var depth = new ReaderDepth(null, [new Source("slow", gate.Task)], null);
        using var h = new LoaderHarness(depth: depth);
        h.Presenter.Auto = false;
        h.Cached("m1");

        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        h.Time.Advance(ReaderDepth.BannerBudget);
        Assert.True(SpinUntil(() => h.Presenter.Pending.Count == 1));

        // the late banner's continuation runs on the pool: give it a moment to land in the loader's stash, then the page is laid out
        gate.SetResult();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.True(h.Presenter.Ready(h.Presenter.Pending[0]));

        var ready = Assert.Single(h.Applies, a => a.BodyState == BodyState.Ready);
        Assert.Equal(["slow"], ready.Banners.Select(b => b.Id));
    }

    [Fact]
    public void A_late_banner_of_a_message_that_was_left_is_dropped()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var depth = new ReaderDepth(null, [new Source("slow", gate.Task, only: "m1")], null);
        using var h = new LoaderHarness(depth: depth);
        h.Cached("m1");
        h.Cached("m2");

        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        h.Time.Advance(ReaderDepth.BannerBudget);
        Assert.True(SpinUntil(() => string.Equals(h.Presenter.Shown, "m1", StringComparison.Ordinal)));
        h.Loader.Open(h.Row(2), OpenReason.Key, ListDirection.Down);
        Assert.True(SpinUntil(() => string.Equals(h.Presenter.Shown, "m2", StringComparison.Ordinal)));

        gate.SetResult();
        Assert.False(SpinWait.SpinUntil(() => h.Reader.Banners.Count > 0, TimeSpan.FromMilliseconds(200)));
        Assert.Equal("m2", h.Reader.EmailId);
    }

    private static bool SpinUntil(Func<bool> done) => SpinWait.SpinUntil(done, TimeSpan.FromSeconds(5));

    private sealed class Source(string id, Task? wait, string? only = null) : IReaderBannerSource
    {
        public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
        {
            if (only is not null && !string.Equals(snapshot.EmailId, only, StringComparison.Ordinal))
            {
                yield break;
            }

            if (wait is not null)
            {
#pragma warning disable VSTHRD003 // the test's gate: a TaskCompletionSource with asynchronous continuations
                await wait.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }

            yield return new ReaderBanner(id, "Banner " + id, null, false, []);
        }
    }
}
