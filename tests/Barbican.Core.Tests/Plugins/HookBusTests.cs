// New tests: the hook bus (Bulwark lib/plugin-hook-catalog.ts kinds; PORT-PLAN M6b: host-agnostic). Owner mailcore.

using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Tests.Plugins;

public sealed class HookBusTests
{
    private sealed record Payload(int N);

    [Fact]
    public async Task Without_handlers_nothing_listens_and_a_call_allocates_nothing()
    {
        var bus = new HookBus();
        Assert.False(bus.HasHandlers(HookNames.OnSearch));
        var payload = new Payload(1);

        // warm up the generic instantiations, then measure
        await RunAsync(bus, payload, 10);
        var before = GC.GetAllocatedBytesForCurrentThread();
        await RunAsync(bus, payload, 10_000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024, $"{allocated} bytes allocated by 10000 calls with no handler");
    }

    private static async Task RunAsync(HookBus bus, Payload payload, int times)
    {
        for (var i = 0; i < times; i++)
        {
            _ = bus.HasHandlers(HookNames.OnSearch);
            await bus.NotifyAsync(HookNames.OnSearch, payload, CancellationToken.None);
            _ = await bus.InterceptAsync(HookNames.OnBeforeShortcut, payload, CancellationToken.None);
            _ = await bus.TransformAsync(HookNames.OnEmailsFetched, payload, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Observers_run_in_registration_order_and_can_be_removed()
    {
        var bus = new HookBus();
        var order = new List<string>();
        using var a = bus.Observe<Payload>(HookNames.OnSearch, (p, _) =>
        {
            order.Add("a" + p.N);
            return ValueTask.CompletedTask;
        });
        var b = bus.Observe<Payload>(HookNames.OnSearch, (p, _) =>
        {
            order.Add("b" + p.N);
            return ValueTask.CompletedTask;
        });
        Assert.True(bus.HasHandlers(HookNames.OnSearch));
        await bus.NotifyAsync(HookNames.OnSearch, new Payload(1), CancellationToken.None);
        b.Dispose();
        await bus.NotifyAsync(HookNames.OnSearch, new Payload(2), CancellationToken.None);
        Assert.Equal(["a1", "b1", "a2"], order);
    }

    [Fact]
    public async Task An_interceptor_that_says_no_stops_the_chain()
    {
        var bus = new HookBus();
        var second = false;
        using var one = bus.Intercept<Payload>(HookNames.OnBeforeShortcut, (_, _) => ValueTask.FromResult(false));
        using var two = bus.Intercept<Payload>(HookNames.OnBeforeShortcut, (_, _) =>
        {
            second = true;
            return ValueTask.FromResult(true);
        });
        Assert.False(await bus.InterceptAsync(HookNames.OnBeforeShortcut, new Payload(1), CancellationToken.None));
        Assert.False(second);
    }

    [Fact]
    public async Task A_transform_chain_feeds_each_result_to_the_next()
    {
        var bus = new HookBus();
        using var one = bus.Transform<Payload>(HookNames.OnEmailsFetched, (p, _) => ValueTask.FromResult(p with { N = p.N + 1 }));
        using var two = bus.Transform<Payload>(HookNames.OnEmailsFetched, (p, _) => ValueTask.FromResult(p with { N = p.N * 10 }));
        Assert.Equal(20, (await bus.TransformAsync(HookNames.OnEmailsFetched, new Payload(1), CancellationToken.None)).N);
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_stop_the_others_or_the_caller()
    {
        var bus = new HookBus();
        Exception? failure = null;
        bus.HandlerFailed += (_, e) => failure = e.Error;
        var ran = false;
        using var bad = bus.Observe<Payload>(HookNames.OnSearch, (_, _) => throw new InvalidOperationException("plugin bug"));
        using var good = bus.Observe<Payload>(HookNames.OnSearch, (_, _) =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });
        await bus.NotifyAsync(HookNames.OnSearch, new Payload(1), CancellationToken.None);
        Assert.True(ran);
        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public async Task A_handler_for_another_payload_type_is_skipped()
    {
        var bus = new HookBus();
        using var other = bus.Transform<string>(HookNames.OnEmailsFetched, (s, _) => ValueTask.FromResult(s + "!"));
        Assert.Equal(7, (await bus.TransformAsync(HookNames.OnEmailsFetched, new Payload(7), CancellationToken.None)).N);
    }

    [Fact]
    public async Task Cancellation_reaches_the_caller()
    {
        var bus = new HookBus();
        using var slow = bus.Observe<Payload>(HookNames.OnSearch, (_, ct) => new ValueTask(Task.Delay(Timeout.Infinite, ct)));
        using var cts = new CancellationTokenSource();
        var running = bus.NotifyAsync(HookNames.OnSearch, new Payload(1), cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WaitAsync(running));
    }

    private static async Task WaitAsync(ValueTask pending) => await pending;
}
