// The message loader's rules (M1-plan §6.5 exit tests; Rust msgload.rs module comment): a cached switch applies header and body in one
// Apply; an uncached one keeps the old message and dims it after 150 ms on a virtual clock; a late result of a superseded open is ignored;
// mark-as-read is never sent by the M1 actions; the delay logic works with actions that may write. Owner body.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.Core.Mail.Body.Hooks;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Tests.Mail.Loading;

public sealed class MessageLoaderTests
{
    [Fact]
    public void A_cached_switch_applies_header_and_body_in_one_apply()
    {
        using var h = new LoaderHarness();
        h.Cached("m1");
        h.Cached("m2");
        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        h.Applies.Clear();
        h.Presenter.Calls.Clear();

        h.Loader.Open(h.Row(2), OpenReason.Key, ListDirection.Down);

        var only = Assert.Single(h.Applies);
        Assert.Equal("m2", only.EmailId);
        Assert.Equal(BodyState.Ready, only.BodyState);
        Assert.Equal("Subject m2", only.Header.Subject);
        Assert.Equal(["show m2", "reveal m2"], h.Presenter.Calls);
        Assert.DoesNotContain(h.Service.BodyRequests, r => !r.Prefetch);
    }

    [Fact]
    public void An_empty_reader_shows_the_rows_header_at_once()
    {
        using var h = new LoaderHarness();
        h.Service.Hold("m3");
        h.Loader.Open(h.Row(3), OpenReason.Click, ListDirection.None);

        Assert.Equal("m3", h.Reader.EmailId);
        Assert.Equal(BodyState.Loading, h.Reader.BodyState);
        Assert.Equal("Subject m3", h.Reader.Header.Subject);
    }

    [Fact]
    public void An_uncached_switch_keeps_the_old_message_and_dims_it_after_150_ms()
    {
        using var h = new LoaderHarness();
        h.Cached("m1");
        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        var gate = h.Service.Hold("m5");
        h.Applies.Clear();
        h.Presenter.Calls.Clear();

        h.Loader.Open(h.Row(5), OpenReason.Click, ListDirection.None);
        Assert.Equal("m1", h.Reader.EmailId);
        Assert.Empty(h.Applies);
        Assert.Equal(["cancel"], h.Presenter.Calls);

        h.Time.Advance(TimeSpan.FromMilliseconds(149));
        Assert.False(h.Presenter.Dimmed);
        Assert.Equal("m1", h.Reader.EmailId);

        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(h.Presenter.Dimmed);
        Assert.True(h.Presenter.Loading);
        Assert.Equal("m5", h.Reader.EmailId);
        Assert.Equal(BodyState.Loading, h.Reader.BodyState);

        gate.SetResult();
        Assert.True(SpinUntil(() => h.Reader.BodyState == BodyState.Ready));
        Assert.Equal("m5", h.Reader.EmailId);
        Assert.False(h.Presenter.Loading);
        Assert.Equal("m5", h.Presenter.Shown);
    }

    [Fact]
    public void Key_repeat_keeps_the_150_ms_timer_running_once_per_burst()
    {
        using var h = new LoaderHarness();
        h.Cached("m0");
        h.Loader.Open(h.Row(0), OpenReason.Click, ListDirection.None);
        h.Service.Hold("m6");
        h.Service.Hold("m7");
        h.Loader.Open(h.Row(6), OpenReason.Key, ListDirection.Down);
        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        h.Loader.Open(h.Row(7), OpenReason.Key, ListDirection.Down);
        h.Time.Advance(TimeSpan.FromMilliseconds(50));

        // 150 ms after the first key of the burst, not after the last one
        Assert.True(h.Presenter.Dimmed);
        Assert.Equal("m7", h.Reader.EmailId);
    }

    [Fact]
    public async Task A_late_result_of_a_superseded_open_is_ignored()
    {
        using var h = new LoaderHarness();
        var slow = h.Service.Hold("m4");
        var fast = h.Service.Hold("m8");
        h.Loader.Open(h.Row(4), OpenReason.Click, ListDirection.None);
        h.Loader.Open(h.Row(8), OpenReason.Click, ListDirection.None);

        fast.SetResult();
        Assert.True(SpinUntil(() => h.Reader.BodyState == BodyState.Ready));
        h.Applies.Clear();
        h.Presenter.Calls.Clear();

        slow.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(h.Applies);
        Assert.DoesNotContain("show m4", h.Presenter.Calls);
        Assert.Equal("m8", h.Reader.EmailId);
    }

    [Fact]
    public void A_page_that_becomes_ready_after_another_open_is_refused()
    {
        using var h = new LoaderHarness();
        h.Cached("m1");
        h.Cached("m2");
        h.Presenter.Auto = false;
        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        var first = Assert.Single(h.Presenter.Pending);
        h.Loader.Open(h.Row(2), OpenReason.Click, ListDirection.None);

        Assert.False(first.OnReady());
        Assert.NotEqual("m1", h.Reader.EmailId);
    }

    [Fact]
    public void A_failed_open_leaves_nothing_of_the_previous_message()
    {
        using var h = new LoaderHarness();
        h.Cached("m1");
        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        h.Service.Bodies.TryRemove("m9", out _);

        h.Loader.Open(h.Row(9), OpenReason.Click, ListDirection.None);

        Assert.True(SpinUntil(() => h.Reader.BodyState == BodyState.Failed));
        Assert.Equal("m9", h.Reader.EmailId);
        Assert.Equal("Subject m9", h.Reader.Header.Subject);
        Assert.Contains("Could not load message", h.Reader.Error, StringComparison.Ordinal);
        Assert.Null(h.Presenter.Shown);
    }

    [Fact]
    public void Close_empties_the_reader_and_the_view()
    {
        using var h = new LoaderHarness();
        h.Cached("m1");
        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        h.Loader.Close();

        Assert.True(h.Reader.IsEmpty);
        Assert.Null(h.Presenter.Shown);
        Assert.Null(h.Loader.ShownId);
    }

    [Fact]
    public void The_m1_actions_never_mark_a_message_read()
    {
        var actions = new ReadOnlyMailActions();
        Assert.False(actions.CanWrite);
        using var h = new LoaderHarness();
        h.Cached("m0");

        // m0 is unread in the list; with the read-only actions nothing is scheduled at all
        h.Loader.Open(h.Row(0), OpenReason.Click, ListDirection.None);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(h.Actions.MarkedRead);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2000, 1999, false)]
    [InlineData(2000, 2000, true)]
    [InlineData(-1, 60000, false)]
    public void Mark_as_read_follows_the_delay_when_writes_are_possible(int delay, int after, bool marked)
    {
        using var h = new LoaderHarness($"{{\"disableThreading\":true,\"markAsReadDelay\":{delay}}}", canWrite: true);
        h.Cached("m0");
        h.Loader.Open(h.Row(0), OpenReason.Click, ListDirection.None);
        h.Time.Advance(TimeSpan.FromMilliseconds(after));
        Assert.Equal(marked, h.Actions.MarkedRead.Contains("m0"));
    }

    [Fact]
    public void A_message_left_before_the_delay_is_not_marked_and_a_read_one_never()
    {
        using var h = new LoaderHarness("{\"disableThreading\":true,\"markAsReadDelay\":2000}", canWrite: true);
        h.Cached("m0");
        h.Cached("m1");
        h.Cached("m2");
        h.Loader.Open(h.Row(0), OpenReason.Click, ListDirection.None);
        h.Time.Advance(TimeSpan.FromMilliseconds(1000));
        h.Loader.Open(h.Row(2), OpenReason.Click, ListDirection.None);
        h.Time.Advance(TimeSpan.FromMilliseconds(1500));
        Assert.DoesNotContain("m0", h.Actions.MarkedRead);

        // m1 is read in the list
        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("m1", h.Actions.MarkedRead);
    }

    [Fact]
    public async Task Prefetch_goes_in_the_direction_of_travel()
    {
        using var h = new LoaderHarness();
        h.Cached("m5");
        h.Cached("m4");
        h.Loader.Open(h.Row(5), OpenReason.Click, ListDirection.None);
        await h.DrainPrefetchAsync();
        var down = h.Service.BodyRequests.Where(r => r.Prefetch).Select(r => r.Id).ToList();

        // first open: two ahead (m6, m7) and one behind (m4, cached: skipped)
        Assert.Equal(["m6", "m7"], down);

        while (h.Service.BodyRequests.TryDequeue(out _))
        {
        }

        // one row up: moving backwards, three ahead (m3, m2, m1) and one behind (m5, cached)
        h.Loader.Open(h.Row(4), OpenReason.Key, ListDirection.Up);
        await h.DrainPrefetchAsync();
        var up = h.Service.BodyRequests.Where(r => r.Prefetch).Select(r => r.Id).ToList();
        Assert.Equal(["m3", "m2", "m1"], up);
        Assert.True(h.Loader.Prefetcher.Travel.Back);
        Assert.True(h.Loader.Prefetcher.Travel.Moving);
    }

    [Fact]
    public async Task A_list_load_warms_its_top_two_messages()
    {
        using var h = new LoaderHarness();
        h.Neighbours.RaiseLoaded();
        await h.DrainPrefetchAsync();
        Assert.Equal(["m0", "m1"], h.Service.BodyRequests.Where(r => r.Prefetch).Select(r => r.Id).ToList());
        Assert.True(h.Cache.TryGet("acct", "m0", out _));
    }

    [Fact]
    public async Task Open_and_close_hooks_fire_for_the_shown_message()
    {
        using var h = new LoaderHarness();
        var opened = new List<string>();
        var closed = new List<string>();
        var rendered = new List<string>();
        using var counted = new SemaphoreSlim(0);
        using var a = h.Bus.Observe<EmailReadView>(HookNames.OnEmailOpen, (v, _) => AddAsync(opened, v.Id, counted));
        using var b = h.Bus.Observe<EmailIdPayload>(HookNames.OnEmailClose, (v, _) => AddAsync(closed, v.EmailId, counted));
        using var c = h.Bus.Observe<EmailContentRenderPayload>(HookNames.OnEmailContentRender, (v, _) => AddAsync(rendered, v.EmailId, counted));
        h.Cached("m1");
        h.Cached("m2");
        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        h.Loader.Open(h.Row(2), OpenReason.Click, ListDirection.None);
        h.Loader.Close();
        for (var i = 0; i < 6; i++)
        {
            Assert.True(await counted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }

        lock (opened)
        {
            Assert.Equal(["m1", "m2"], opened.Order(StringComparer.Ordinal));
            Assert.Equal(["m1", "m2"], closed.Order(StringComparer.Ordinal));
            Assert.Equal(["m1", "m2"], rendered.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void A_body_plugin_can_take_the_body_over()
    {
        using var h = new LoaderHarness();
        using var t = h.Bus.Transform<RenderableBody>(HookNames.OnRenderEmailBody, (v, _) => ValueTask.FromResult(v with { Html = "<p>decrypted</p>", HandledBy = "pgp" }));
        h.Cached("m1");
        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);
        Assert.True(SpinUntil(() => string.Equals(h.Presenter.Shown, "m1", StringComparison.Ordinal)));
        Assert.Equal("<p>decrypted</p>", h.Presenter.LastRequest?.Content.Html);
        Assert.Equal("m1", h.Reader.EmailId);
    }

    private static ValueTask AddAsync(List<string> list, string id, SemaphoreSlim counted)
    {
        lock (list)
        {
            list.Add(id);
        }

        counted.Release();
        return ValueTask.CompletedTask;
    }

    private static bool SpinUntil(Func<bool> done) => SpinWait.SpinUntil(done, TimeSpan.FromSeconds(5));
}
